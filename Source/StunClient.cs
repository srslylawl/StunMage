using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace STUN {
    public class StunClient : IDisposable {
        public int ConnectionAttempts;
        public double ConnectionTimeout;
        private Socket Socket;
        public Action<string> Log;

        private IPAddress ip_stunServer_primary;
        private IPAddress ip_stunServer_secondary;

        private int port_stunServer_primary;
        private int port_stunServer_secondary;

        public IPAddress publicIPAddress;
        public OutboundBehaviorTest IncomingQueryTest;


        public STUN_NetType NATType;

        public StunClient(IPEndPoint stunServerEndPoint, int connectionAttempts = 3, double connectionTimeout = 2.0, Action<string> log = null) {
            ip_stunServer_primary = stunServerEndPoint.Address;
            port_stunServer_primary = stunServerEndPoint.Port;
            ConnectionAttempts = connectionAttempts;
            ConnectionTimeout = connectionTimeout;
            Log = log;
            Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            Socket.ReceiveTimeout = (int)ConnectionTimeout * 1000;
            Socket.SendTimeout = (int)ConnectionTimeout * 1000;
            Socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        }

        ~StunClient() {
            Dispose(false);
        }
        
        public async Task<StunMessage> BasicBindingRequest(IPEndPoint endPoint, STUN_ChangeRequest changeRequest = null) {
            StunMessage test = new StunMessage {
                ChangeRequest = changeRequest
            };
            return await DoTransaction(Socket, test, endPoint);
        }

        public async Task<IPAddress> GetPublicIP() {
            IPEndPoint endPoint_primaryServerPrimaryPoint = new IPEndPoint(ip_stunServer_primary, port_stunServer_primary);
            var response = await BasicBindingRequest(endPoint_primaryServerPrimaryPoint);
            if (response == null) {
                Log?.Invoke("===========");
                Log?.Invoke("No response at all - UDP seems to be blocked.");
                Log?.Invoke("===========");
                NATType = STUN_NetType.UDP_blocked;
                return IPAddress.None;
            }

            return response.MappedAddress.Address;
        }

        public async Task TryQueryIncomingNATType() {
            //Init Request
            IPEndPoint endPoint_primaryServerPrimaryPoint = new IPEndPoint(ip_stunServer_primary, port_stunServer_primary);
            var response = await BasicBindingRequest(endPoint_primaryServerPrimaryPoint);
            if (response == null) {
                Log?.Invoke("===========");
                Log?.Invoke("No response at all - UDP seems to be blocked.");
                Log?.Invoke("===========");
                NATType = STUN_NetType.UDP_blocked;
                return;
            }
            
            IPEndPoint publicEndPoint = response.MappedAddress;
            publicIPAddress = publicEndPoint.Address;
            ip_stunServer_secondary = response.ChangedAddress.Address;
            port_stunServer_secondary = response.ChangedAddress.Port;

            Log?.Invoke($"Secondary Address: {ip_stunServer_secondary}");
            Log?.Invoke($"Secondary Port: {port_stunServer_secondary}");
            Log?.Invoke($"External endpoint used: {publicEndPoint}");
            Log?.Invoke($"Local endpoint used: {Socket.LocalEndPoint}");


            OutboundBehaviorTest outboundBehaviorTest = new OutboundBehaviorTest();
            outboundBehaviorTest.LocalEndPoint = (IPEndPoint)Socket.LocalEndPoint;
            outboundBehaviorTest.External_1_1 = publicEndPoint;

            if (Socket.LocalEndPoint.Equals(publicEndPoint)) {
                // No NAT
                Log?.Invoke("Local endpoint is same as external endpoint; no NAT detected.");
                StunMessage noNAT_response_from_different_ip_and_port =
                    await BasicBindingRequest(new IPEndPoint(ip_stunServer_primary, port_stunServer_primary), new STUN_ChangeRequest(true, true));
                Debug.WriteLine("Testing if response from different IP and port arrives..");
                if (noNAT_response_from_different_ip_and_port != null) {
                    // Open Internet.
                    Log?.Invoke("===========");
                    Log?.Invoke("Received - Open Internet!");
                    Log?.Invoke("===========");

                    NATType = STUN_NetType.Open_Internet;
                    return;
                }
                // Symmetric UDP firewall.
                Log?.Invoke("===========");
                Log?.Invoke("Not received - Symmetric UDP Firewall.");
                Log?.Invoke("===========");
                NATType = STUN_NetType.Symmetric_UDP_Firewall;
                return;
            }
            
            Log?.Invoke("Local and external endpoints mismatch -> NAT present. Probing NAT type...");
            StunMessage response_from_different_ip_and_port =
                await BasicBindingRequest(endPoint_primaryServerPrimaryPoint, new STUN_ChangeRequest(true, true));
            
            Log?.Invoke("Requesting response from different IP and port...");
            if (response_from_different_ip_and_port != null) {
                // Full cone NAT.
                Log?.Invoke($"Response received from {response_from_different_ip_and_port.SourceAddress}!");
                Log?.Invoke("===========");
                Log?.Invoke("Either port is forwarded or NAT Type is 'Full Cone'");
                Log?.Invoke("===========");
                NATType = STUN_NetType.Full_Cone;
                return;
            }
            Log?.Invoke("No response received from different IP and port.");
            
            // No Response - Testing response from secondary server to check if external endpoint mapping remains the same
            
            Log?.Invoke($"Requesting direct response from secondary STUN Server endpoint...");
            IPEndPoint endPoint_secondaryServerPrimaryPort = new IPEndPoint(ip_stunServer_secondary, port_stunServer_primary);
            StunMessage response_from_secondary_server = await BasicBindingRequest(endPoint_secondaryServerPrimaryPort);
            if (response_from_secondary_server == null) {
                //should always get a response unless udp is suddenly blocked or server has any issues
                Log?.Invoke("===========");
                Log?.Invoke("Didn't get a response - UDP blocked or secondary server down?");
                Log?.Invoke("===========");
                NATType = STUN_NetType.UDP_blocked;
                return;
            }
            
            if (response_from_secondary_server.MappedAddress.Equals(publicEndPoint)) {
                Log?.Invoke($"Response received - requesting response from alternative port...");
                StunMessage response_from_different_port = await BasicBindingRequest(endPoint_secondaryServerPrimaryPort, new STUN_ChangeRequest(false, true));
                if (response_from_different_port != null) {
                    NATType = STUN_NetType.Restricted_Cone;
                    Log?.Invoke($"Response received. NAT Type: 'Restricted Cone' - can receive packets from different port if a previous packet was sent to same IP");
                    return;
                }
                Log?.Invoke("===========");
                Log?.Invoke("No response. NAT Type: 'Port Restricted Cone' - can only received packets from same IP and port a previous package was sent to.");
                Log?.Invoke("===========");
                NATType = STUN_NetType.Port_Restricted_Cone;
                return;
            }
            
            Log?.Invoke($"Response received, but NAT allocated a different endpoint: ({response_from_secondary_server.MappedAddress}); NAT Type: 'Symmetric'");
            NATType = STUN_NetType.Symmetric;

            outboundBehaviorTest.External_2_1 = response_from_secondary_server.MappedAddress;

            IPEndPoint endPoint_primaryServer_secondaryPort = new IPEndPoint(ip_stunServer_primary, port_stunServer_secondary);

            Log?.Invoke($"Trying to probe outgoing port binding behavior... ({response_from_secondary_server.MappedAddress}); NAT Type: 'Symmetric'");
            var response_from_primary_with_alt_port = await BasicBindingRequest(endPoint_primaryServer_secondaryPort);

            if (response_from_primary_with_alt_port == null) {
                Log?.Invoke("Unable to probe - response from primary server with alternate port not received.");
                return;
            }

            outboundBehaviorTest.External_1_2 = response_from_primary_with_alt_port.MappedAddress;
            
            IPEndPoint endPoint_secondaryServer_secondaryPort = new IPEndPoint(ip_stunServer_secondary, port_stunServer_secondary);
            var response_from_secondary_with_alt_port = await BasicBindingRequest(endPoint_secondaryServer_secondaryPort);
            
            if (response_from_secondary_with_alt_port == null) {
                Log?.Invoke("Unable to probe - response from secondary server with alternate port not received.");
                return;
            }

            outboundBehaviorTest.External_2_2 = response_from_secondary_with_alt_port.MappedAddress;

            IncomingQueryTest = outboundBehaviorTest;
        }

        public async Task<OutboundBehaviorTest> ConductBehaviorTest(IPEndPoint serverEndPoint) {
            using (Socket tempSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)) {
                tempSocket.ReceiveTimeout = (int)ConnectionTimeout * 1000;
                tempSocket.SendTimeout = (int)ConnectionTimeout * 1000;
                tempSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

                IPAddress[] ipAddresses = new IPAddress[2];
                int[] ports = new int[2];

                ipAddresses[0] = serverEndPoint.Address;
                ports[0] = serverEndPoint.Port;

                OutboundBehaviorTest test = new OutboundBehaviorTest();

                for (int server = 0; server < 2; ++server) {
                    for (int port = 0; port < 2; ++port) {
                        IPEndPoint endPoint = new IPEndPoint(ipAddresses[server], ports[port]);
                        var res = await DoTransaction(tempSocket, new StunMessage(), endPoint);
                        if (res == null) {
                            Log?.Invoke($"ERROR: No response from {endPoint} ({(server == 0 ? "primary" : "secondary")} ip with {(port == 0 ? "primary" : "secondary")} port) - Unable to conduct Outbound Behavior Test.");
                            res.MappedAddress = new IPEndPoint(IPAddress.None, 0);
                        }
                        if (server == 0 && port == 0) {
                            var changedAddr = res.ChangedAddress;
                            if (changedAddr == null) {
                                throw new Exception("Server did not respond with an alternative address!");
                            }
                            ipAddresses[1] = res.ChangedAddress.Address;
                            ports[1] = res.ChangedAddress.Port;
                        }
                        
                        test.SetEndPoint(server, port, res.MappedAddress);
                    }
                }

                test.LocalEndPoint = (IPEndPoint)tempSocket.LocalEndPoint;

                return test;
            }
        }

        public async Task<StunMessage> DoTransaction(Socket socket, StunMessage request, IPEndPoint remoteEndPoint) {
            await Task.Yield();
            byte[] requestBytes = request.ToByteData();
            for (int i = 0; i < ConnectionAttempts; i++) {
                socket.SendTo(requestBytes, remoteEndPoint);
                DateTime tryUntil = DateTime.Now.AddSeconds(ConnectionTimeout);
                while (tryUntil > DateTime.Now) {
                    if (socket.Poll(100, SelectMode.SelectRead)) {
                        //received 
                        byte[] receiveBuffer = new byte[512];
                        ArraySegment<byte> receiveBufferSegment = new ArraySegment<byte>(receiveBuffer);
                        await socket.ReceiveAsync(receiveBufferSegment, SocketFlags.None);
                        //socket.Receive(receiveBuffer);

                        // Parse message
                        StunMessage response = new StunMessage();
                        response.Parse(receiveBuffer);

                        if (request.TransactionID.Equals(response.TransactionID)) {
                            return response;
                        }

                        Log?.Invoke($"Received transaction ID {response.TransactionID} does not match transaction ID {request.TransactionID}");
                    }
                    await Task.Delay(1);
                }
                int remaining = ConnectionAttempts - i - 1;
                if (remaining > 0) {
                    Log?.Invoke($"No response - timed out. Attempts left: {remaining}.");
                }
            }
            Log?.Invoke($"Request to {remoteEndPoint} timed out.");
            return null;
        }


        private void Dispose(bool disposing) {
            if (disposing) {
                Socket?.Dispose();
            }
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}