
using Mono.Nat;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace STUN {
    public class StunMage {
        public event Action<string> LogFunctionVerbose;
        public event Action<string> LogFunction;

        public bool HolePunchInProgress { get; private set;}

        public int ConnectionAttempts = 3;
        public float ConnectionTimeout = 2.0f;

        public float HolePunchSendInterval = 2.0f;

        public STUN_NetType NATType { get; private set; }
        public OutboundBehaviorType OutboundBehaviorType { get; private set; }

        public EndPointBehaviorTuple EndPointBehaviorTuple { get; private set; }

        public string PublicIPString = "";

        public string HolePunchPassword = "Hello!";

        public bool PasswordHasToMatch;

        public bool TryResolveHostName(string host, out IPAddress address) {
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
        public async Task<bool> TryPortForwarding(ushort port) {
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

        public async Task CheckNATType(IPEndPoint stunServer_primary, IPEndPoint stunServer_secondary) {
            try {
                using (StunClient stunClient = new StunClient(stunServer_primary, ConnectionAttempts, ConnectionTimeout, (s) => Log(s))) {
                    await stunClient.QueryNATType();

                    NATType = stunClient.NATType;

                    if (NATType == STUN_NetType.UDP_blocked) {
                        return;
                    }

                    PublicIPString = stunClient.publicIPAddress.ToString();

                    OutboundBehaviorTest test1 = stunClient.OutboundBehaviorTest;

                    if (test1 == null) {
                        test1 = await stunClient.ConductBehaviorTest(stunServer_primary);
                    }

                    Log(test1.ToString());
                    OutboundBehaviorTest test2 = await stunClient.ConductBehaviorTest(stunServer_secondary);

                    Log(test2.ToString());

                    //analyze behavior tests
                    var outboundBehaviorType = OutboundBehaviorTest.OutBoundBehaviorIsPredictable(test1, test2);
                    switch (outboundBehaviorType) {
                        case OutboundBehaviorType.Predictable_And_Consistent:
                            Log("External endpoints match local endpoints' port and remain the same for different remote IP's and ports.");
                            break;
                        case OutboundBehaviorType.Predictable_Once_Per_IP:
                            Log("External endpoints match local endpoints' port for one remote IP only.");
                            break;
                        case OutboundBehaviorType.Predictable_Once:
                            Log("External endpoints match local endpoints' port for one remote IP and port combination only.");
                            break;
                        case OutboundBehaviorType.UnpredictableButConsistent:
                            Log("External endpoints don't match local endpoint's port but remain consistent for different remote IP's and ports.");
                            break;
                        case OutboundBehaviorType.UnpredictableButConsistent_Per_IP:
                            Log("External endpoints don't match local endpoint's port but remain consistent for the first remote IP only.");
                            break;
                        case OutboundBehaviorType.Unpredictable:
                            Log("External endpoints don't match local endpoint's port and are unpredictable.");
                            break;
                    }

                    OutboundBehaviorType = outboundBehaviorType;

                    EndPointBehaviorTuple behaviorTuple = new EndPointBehaviorTuple() { 
                        IncomingBehaviorGroup = NatTypeToIncomingBehaviorGroup(NATType), 
                        OutgoingBehaviorGroup = OutBoundBehaviorTypeToGroup(outboundBehaviorType)};

                    EndPointBehaviorTuple = behaviorTuple;
                }
            }
            finally {
                Log("======= Done ========");
            }
        }

        private async Task startHolePunch(IPEndPoint peerEndPoint, ushort localPort, Action<IPEndPoint> onHolePunchSuccess) {
            try {
                using (var holePunchSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)) {
                    holePunchSocket.Bind(new IPEndPoint(IPAddress.Any, localPort));
                    await doHolePunch(holePunchSocket, peerEndPoint, onHolePunchSuccess);
                }
            }
            catch (Exception E) {
                Log($"{E.GetType().Name}:{E.Message}");
            }
        }

        private async Task doHolePunch(Socket socket, IPEndPoint remoteEndPoint, Action<IPEndPoint> onHolePunchSuccess) {
            bool readyOrResponseReceived = false;

            Log($"Now listening for packets at local endpoint: {socket.LocalEndPoint}!");
            Log($"Now sending request packets to peer: {remoteEndPoint} at interval of {HolePunchSendInterval} seconds");

            var message_out = new HolePunchMessage {
                type = HolePunchMessage.MessageType.Request
            };
            message_out.Password = HolePunchPassword;
            var sendData = message_out.ToByteArray();
            var nextSendTime = DateTime.Now;

            while (HolePunchInProgress) {
                if (nextSendTime <= DateTime.Now) {
                    if (readyOrResponseReceived) {
                        HolePunchInProgress = false;
                        onHolePunchSuccess?.Invoke(remoteEndPoint);
                        break;
                    }
                    nextSendTime = DateTime.Now.AddSeconds(HolePunchSendInterval);
                    if (HolePunchPassword != message_out.Password) {
                        message_out.Password = HolePunchPassword;
                        sendData = message_out.ToByteArray();
                    }
                    try {
                        socket.SendTo(sendData, remoteEndPoint);
                        Log($"Sent packet to peer.");
                    }
                    catch (Exception E) {
                        Log($"Error while sending packet to peer: {E.Message}");
                    }
                }
                try {
                    if (socket.Poll(100, SelectMode.SelectRead)) {
                        //received 
                        byte[] receiveBuffer = new byte[512];

                        EndPoint sourceEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        socket.ReceiveFrom(receiveBuffer, ref sourceEndPoint);

                        // Parse message
                        HolePunchMessage incomingMessage = new HolePunchMessage();
                        incomingMessage.Parse(receiveBuffer);
                        switch (incomingMessage.type) {
                            case HolePunchMessage.MessageType.None:
                                Log($"Empty message received from: {sourceEndPoint}");
                                break;
                            case HolePunchMessage.MessageType.Request:
                                Log($"[Incoming] Request received from: {sourceEndPoint}! ");
                                var match = incomingMessage.Password == HolePunchPassword;
                                if (!PasswordHasToMatch || match) {
                                    HolePunchMessage response = new HolePunchMessage {
                                        type = HolePunchMessage.MessageType.Response,
                                        mirroredEndPoint = (IPEndPoint)sourceEndPoint
                                    };
                                    Log($"{(PasswordHasToMatch? "Passwords match! " : "")}Sending reply...");
                                    socket.SendTo(response.ToByteArray(), sourceEndPoint);
                                    remoteEndPoint = (IPEndPoint)sourceEndPoint;
                                    //Remote endpoint can theoretically change if we pinged wrong port but only needed to send to peer ip to open our nat
                                }
                                else {
                                    Log($"Password does not match HolePunchPassword - sending no reply.");
                                }
                                break;
                            case HolePunchMessage.MessageType.Response:
                                if (!readyOrResponseReceived) {
                                    Log($"[Incoming] Response received from: {sourceEndPoint}! Mirrored external endpoint: {incomingMessage.mirroredEndPoint}" +
                                                $"{(string.IsNullOrWhiteSpace(incomingMessage.Password) ? "" : $" Payload: {incomingMessage.Password}")}");

                                    message_out.type = HolePunchMessage.MessageType.Ready;
                                    message_out.mirroredEndPoint = (IPEndPoint)sourceEndPoint;
                                    nextSendTime = DateTime.Now.AddSeconds(HolePunchSendInterval);
                                }
                                readyOrResponseReceived = true;
                                break;
                            case HolePunchMessage.MessageType.Ready:
                                //Done here
                                Log($"[Success!] Ready received from: {sourceEndPoint}");
                                if (!readyOrResponseReceived) {
                                    //done here, send ready for 1 more sec then return
                                    message_out.type = HolePunchMessage.MessageType.Ready;
                                    message_out.mirroredEndPoint = (IPEndPoint)sourceEndPoint;
                                    nextSendTime = DateTime.Now.AddSeconds(HolePunchSendInterval);
                                }
                                readyOrResponseReceived = true;
                                break;
                        }
                    }
                }
                catch (Exception e) {
                    Log($"Error: {e.Message}");
                }
                await Task.Delay(1);
            }
        }

        /// <summary>
        /// Starts hole-punching.
        /// Runs until manually cancelled or until success.
        /// Local port is not guaranteed to be bound to matching External Port - if external port is unpredictable but queriable, use an existing socket instead.
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="remoteEndPoint"></param>
        /// <param name="onHolePunchSuccess">Invoked on success, contains potentially different remote endpoint</param>
        /// <returns></returns>
        public async void StartHolePunch(ushort localPort, IPEndPoint remoteEndPoint, Action<IPEndPoint> onSuccess) {
            if (HolePunchInProgress) {
                Log("Hole-punch already in progress.", true);
                return;
            }
            HolePunchInProgress = true;
            await startHolePunch(remoteEndPoint, localPort, onSuccess);
        }

        /// <summary>
        /// Starts hole-punching with an existing socket which will not be disposed.
        /// Runs until manually cancelled or until success.
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="remoteEndPoint"></param>
        /// <param name="onHolePunchSuccess">Invoked on success, contains potentially different remote endpoint</param>
        /// <returns></returns>
        public async void StartHolePunch(Socket socket, IPEndPoint remoteEndPoint, Action<IPEndPoint> onSuccess) {
            if (HolePunchInProgress) {
                Log("Hole-punch already in progress.", true);
                return;
            }
            HolePunchInProgress = true;
            await doHolePunch(socket, remoteEndPoint, onSuccess);
        }

        /// <summary>
        /// Returns bound Socket with a queried external endpoint.
        /// </summary>
        /// <param name="stunServerEndPoint"></param>
        /// <returns></returns>
        /// <exception cref="Exception">Throws if socket fails to bind or unable to query public ip endpoint through stun server.</exception>
        public async Task<(Socket socket, IPEndPoint externalEndPoint)> GetSocketWithQueriedExternalEndPoint(IPEndPoint stunServerEndPoint, ushort customListenPort = 0) {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = (int)ConnectionTimeout * 1000;
            socket.SendTimeout = (int)ConnectionTimeout * 1000;
            socket.Bind(new IPEndPoint(IPAddress.Any, customListenPort));


            var result = await QueryPublicIPEndPoint(stunServerEndPoint, socket);
            if (result.Address == IPAddress.None) {
                throw new Exception("Unable to query external endpoint.");
            }
            return (socket, result);
        }

        /// <summary>
        /// Returns public endpoint. Since allocated local port is random, this is probably only useful to get External IP Address.
        /// </summary>
        /// <param name="stunServerEndPoint"></param>
        /// <returns></returns>
        public async Task<IPEndPoint> QueryPublicIPEndPoint(IPEndPoint stunServerEndPoint) {
            using (StunClient stunClient = new StunClient(stunServerEndPoint, ConnectionAttempts, ConnectionTimeout, Log)) {
                var result = await stunClient.GetPublicEndPoint();
                return result;
            }
        }

        /// <summary>
        /// Query mapped endpoint of a specific socket - will not be disposed.
        /// </summary>
        /// <param name="stunServerEndPoint"></param>
        /// <param name="socket">Socket to query, has to already be bound to a local endpoint</param>
        /// <returns></returns>
        public async Task<IPEndPoint> QueryPublicIPEndPoint(IPEndPoint stunServerEndPoint, Socket socket) {
            using (StunClient stunClient = new StunClient(socket, stunServerEndPoint, ConnectionAttempts, ConnectionTimeout, Log)) {
                var result = await stunClient.GetPublicEndPoint();
                return result;
            }
        }

        public void StopHolePunch() {
            HolePunchInProgress = false;
        }
        private void Log(string message) => Log(message, true);

        private void Log(string message, bool verbose = true) {
            if (!verbose) {
                LogFunction?.Invoke(message);
            }
            LogFunctionVerbose?.Invoke(message);
        }

        public static IncomingBehaviorGroup NatTypeToIncomingBehaviorGroup(STUN_NetType stunType) {
            switch (stunType) {
                case STUN_NetType.UDP_blocked:
                    return IncomingBehaviorGroup.E_Blocked;
                case STUN_NetType.Open_Internet:
                    return IncomingBehaviorGroup.A_Open;
                case STUN_NetType.Symmetric_UDP_Firewall:
                    return IncomingBehaviorGroup.D_RequiresSendToIPAndPort;
                case STUN_NetType.Full_Cone:
                    return IncomingBehaviorGroup.B_RequiresMapping;
                case STUN_NetType.Restricted_Cone:
                    return IncomingBehaviorGroup.C_RequiresSendToIP;
                case STUN_NetType.Port_Restricted_Cone:
                    return IncomingBehaviorGroup.D_RequiresSendToIPAndPort;
                case STUN_NetType.Symmetric:
                    return IncomingBehaviorGroup.D_RequiresSendToIPAndPort;
                default: throw new ArgumentOutOfRangeException(nameof(stunType));
            }
        }

        public static OutgoingBehaviorGroup OutBoundBehaviorTypeToGroup(OutboundBehaviorType type) {
            switch (type) {
                case OutboundBehaviorType.Predictable_And_Consistent:
                case OutboundBehaviorType.Predictable_Once_Per_IP:
                case OutboundBehaviorType.Predictable_Once:
                    return OutgoingBehaviorGroup.Predictable;

                case OutboundBehaviorType.UnpredictableButConsistent:
                   return OutgoingBehaviorGroup.Queryable;

                case OutboundBehaviorType.UnpredictableButConsistent_Per_IP:
                case OutboundBehaviorType.Unpredictable:
                    return OutgoingBehaviorGroup.Unpredictable;

                default: throw new ArgumentException(nameof(type));
            }
        }
    }
}
