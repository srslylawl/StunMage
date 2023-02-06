
using Mono.Nat;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace STUN {
    public class StunMage {
        public event Action<string> LogFunction;
        private Socket holePunchSocket;
        private bool holePunchInProgress;

        public int ConnectionAttempts = 3;
        public float ConnectionTimeout = 2.0f;

        public string NATType = "";
        public string PublicIPString = "";

        public string Payload = "Hello!";

        public short IncomingPort = 7777;

        private bool TryResolveHostName(string host, out IPAddress address) {
            address = IPAddress.None;
            try {
                var addresses = Dns.GetHostAddresses(host);
                for (int i = 0; i < addresses.Length; i++) {
                    var adr = addresses[i];
                    if (adr.AddressFamily == AddressFamily.InterNetwork) {
                        address = adr;
                        break;
                    }
                }
            }
            catch (Exception e) {
                Log($"Unable to resolve hostname '{host}': {e.Message}");
                return false;
            }

            return address != null;
        }
        public async Task<bool> TryPortForwarding(int port) {
            try {
                Log("Asking NAT device (your router) to port-forward using UPnP. Looking for NAT device...");
                PortForwarder portForwarder = new PortForwarder();
                var natDeviceFound = await portForwarder.DiscoverDevicesAsync(Log);
                if (natDeviceFound) {
                    Log("NAT Device found! Asking to port-forward...");
                    return portForwarder.ForwardPort(MappingProtocol.Udp, port, Log);
                }

                Log("Unable to find NAT Device.");
            }
            finally {
                NatUtility.StopDiscovery();
            }

            return false;
        }

        private async Task StartHolePunch(Action<string> logFunc, float sendInterval, IPEndPoint peerEndPoint, short incomingPort) {
            try {
                holePunchSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                holePunchSocket.Bind(new IPEndPoint(IPAddress.Any, incomingPort));
                logFunc?.Invoke($"Now listening for packets at local endpoint: {holePunchSocket.LocalEndPoint}!");
                logFunc?.Invoke($"Now sending request packets to peer: {peerEndPoint} at interval of {sendInterval} seconds");

                var message_out = new HolePunchMessage {
                    type = HolePunchMessage.MessageType.Request
                };
                message_out.Payload = Payload;
                var sendData = message_out.ToByteArray();
                var nextSendTime = DateTime.Now;

                while (holePunchInProgress) {
                    if (nextSendTime <= DateTime.Now) {
                        nextSendTime = DateTime.Now.AddSeconds(sendInterval);
                        if (Payload != message_out.Payload) {
                            message_out.Payload = Payload;
                            sendData = message_out.ToByteArray();
                        }

                        holePunchSocket.SendTo(sendData, peerEndPoint);
                        logFunc?.Invoke($"Sent packet to peer.");
                    }

                    if (holePunchSocket.Poll(100, SelectMode.SelectRead)) {
                        //received 
                        byte[] receiveBuffer = new byte[512];

                        EndPoint sourceEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        holePunchSocket.ReceiveFrom(receiveBuffer, ref sourceEndPoint);

                        // Parse message
                        HolePunchMessage packet = new HolePunchMessage();
                        packet.Parse(receiveBuffer);
                        switch (packet.type) {
                            case HolePunchMessage.MessageType.None:
                                logFunc?.Invoke($"Empty message received from: {sourceEndPoint}");
                                break;
                            case HolePunchMessage.MessageType.Request:
                                logFunc?.Invoke(
                                    $"[Incoming] Request received from: {sourceEndPoint}! " +
                                    $"{(string.IsNullOrWhiteSpace(packet.Payload) ? "" : $"Payload: '{packet.Payload}' | ")}Sending Response..");
                                HolePunchMessage response = new HolePunchMessage {
                                    type = HolePunchMessage.MessageType.Response,
                                    mirroredEndPoint = (IPEndPoint)sourceEndPoint
                                };
                                holePunchSocket.SendTo(response.ToByteArray(), sourceEndPoint);
                                break;
                            case HolePunchMessage.MessageType.Response:
                                logFunc?.Invoke($"[Incoming] Response received from: {sourceEndPoint}! Mirrored external endpoint: {packet.mirroredEndPoint}" +
                                                $"{(string.IsNullOrWhiteSpace(packet.Payload) ? "" : $" Payload: {packet.Payload}")}");
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }

                    await Task.Delay(1);
                }
            }
            catch (Exception E) {
                logFunc?.Invoke($"{E.GetType().Name}:{E.Message}");
            }

            finally {
                holePunchSocket.Dispose();
                holePunchSocket = null;
                logFunc?.Invoke("Stopped listening for packets.");
            }
        }

        public async Task CheckNATType(string stunServerPrimary, int portPrimary, string stunServerSecondary, int portSecondary) {
            if (string.IsNullOrWhiteSpace(stunServerPrimary)) {
                Log("No STUN server specified.");
                return;
            }

            try {
                Log($"Trying to resolving host name: '{stunServerPrimary}' ...");
                if (!TryResolveHostName(stunServerPrimary, out IPAddress stun_PrimaryIPAddress)) {
                    Log("Failed to resolve host name! Double-check host name and DNS settings. Perhaps try a different Server.");
                    return;
                }

                Log($"STUN server IP obtained: {stun_PrimaryIPAddress}. Sending request..");

                IPEndPoint primaryStunServer = new IPEndPoint(stun_PrimaryIPAddress, portPrimary);
                using (StunClient stunClient = new StunClient(primaryStunServer, ConnectionAttempts, ConnectionTimeout, Log)) {
                    await stunClient.TryQueryIncomingNATType();

                    NATType = stunClient.NATType.ToString();

                    if (stunClient.NATType == STUN_NetType.UdpBlocked) {
                        return;
                    }

                    PublicIPString = stunClient.publicIPAddress.ToString();

                    OutboundBehaviorTest test1 = stunClient.IncomingQueryTest;

                    if (test1 == null) {
                        test1 = await stunClient.ConductBehaviorTest(primaryStunServer);
                    }

                    Log(test1.ToString());

                    string hostName = stunServerSecondary;
                    Log($"Trying to resolving host name: '{stunServerSecondary}' ...");
                    if (!TryResolveHostName(hostName, out IPAddress stun_alt)) {
                        Log($"Failed to resolve host name! Double-check host name and DNS settings. Perhaps try a different Server.");
                        return;
                    }

                    IPEndPoint altStunServer = new IPEndPoint(stun_alt, portSecondary);
                    OutboundBehaviorTest test2 = await stunClient.ConductBehaviorTest(altStunServer);

                    Log(test2.ToString());

                    //analyze behavior tests
                    bool predictable = OutboundBehaviorTest.OutBoundBehaviorIsPredictable(test1, test2);
                    Log(predictable
                        ? "External endpoints match internal endpoint and stay consistent after sending packets to different IP. - Hole-punching should work!"
                        : "External endpoints are inconsistent.");
                }
            }
            finally {
                Log("======= Done ========");
            }
        }

        public async void StartHolePunch(string ip_peer, int portPeer) {
            if (holePunchInProgress) {
                holePunchInProgress = false;
            }
            else {
                if (string.IsNullOrWhiteSpace(ip_peer)) {
                    Log("Enter a valid peer IP Address.");
                    return;
                }

                int peerPort = portPeer;
                if (!IPAddress.TryParse(ip_peer, out IPAddress peerIP)) {
                    Log("Invalid Peer IP.");
                    return;
                }

                holePunchInProgress = true;

                await StartHolePunch(Log, ConnectionTimeout, new IPEndPoint(peerIP, peerPort), IncomingPort);
            }
        }

        public void StopHolePunch() {
            holePunchInProgress = false;
        }

        private void Log(string message) {
            LogFunction?.Invoke(message);
        }
    }
}
