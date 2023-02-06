using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

using Mono.Nat;

// ReSharper disable RedundantNameQualifier
// ReSharper disable RedundantDelegateCreation


namespace STUN {
	/// <summary>
	/// Application main window.
	/// </summary>
	public class winFormMain : Form {
		private System.Windows.Forms.Label Label_PortToFree;
		private System.Windows.Forms.Label Label_STUN_Server;
		private System.Windows.Forms.Label Label_ConnectionAttempts;
		private System.Windows.Forms.Label Label_TimeOut;

		private System.Windows.Forms.TextBox Input_StunServer;
		private System.Windows.Forms.NumericUpDown Input_StunServer_Port;
		private System.Windows.Forms.NumericUpDown Input_PortToFree;
		private System.Windows.Forms.NumericUpDown Input_ConnectionAttempts;
		private System.Windows.Forms.NumericUpDown Input_TimeOut;
		private IContainer components;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.Button Button_PortForward;
		private System.Windows.Forms.Button Button_CheckNATType;
		private System.Windows.Forms.Button Button_HolePunch;
		private System.Windows.Forms.Label Label_PeerAddress;
		private System.Windows.Forms.TextBox Input_HolePunch_Address;
		private System.Windows.Forms.NumericUpDown Input_HolePunch_Port;
		private System.Windows.Forms.TextBox TextBox_Output;

		private System.Windows.Forms.Label Label_PublicIP;
		private System.Windows.Forms.TextBox TextBox_PublicIP;


		private Socket holePunchSocket;
		private string publicIPString;

		private bool holePunchInProgress;


		public winFormMain() {
			InitializeComponent();
		}

		private void Log(string str) {
			TextBox_Output.AppendText($"{str}\r\n");
		}

		bool TryResolveHostName(string host, out IPAddress address) {
			address = null;
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

		private async Task<bool> TryPortForwarding(int port) {
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
				// Log("Stopped Nat Discovery.");
				NatUtility.StopDiscovery();
			}

			return false;
		}


		private async Task StartHolePunch(Action<string> logFunc, float sendInterval, IPEndPoint peerEndPoint) {
			try {
				holePunchSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
				holePunchSocket.Bind(new IPEndPoint(IPAddress.Any, (int)Input_PortToFree.Value));
				logFunc?.Invoke($"Now listening for packets at local endpoint: {holePunchSocket.LocalEndPoint}!");
				logFunc?.Invoke($"Now sending request packets to peer: {peerEndPoint} at interval of {sendInterval} seconds");

				var message_out = new HolePunchMessage {
					type = HolePunchMessage.MessageType.Request
				};
				message_out.Payload = Input_Payload.Text;
				var sendData = message_out.ToByteArray();
				var nextSendTime = DateTime.Now;

				while (holePunchInProgress) {
					if (nextSendTime <= DateTime.Now) {
						nextSendTime = DateTime.Now.AddSeconds(sendInterval);
						if (Input_Payload.Text != message_out.Payload) {
							message_out.Payload = Input_Payload.Text;
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

		private async void Button_PortForward_Click(object sender, EventArgs e) {
			MakeElementsInteractable(false);
			await Task.Delay(1);
			int port = Convert.ToInt32(Input_PortToFree.Value);

			bool forwarded = await TryPortForwarding(port);
			if (forwarded) {
				Log("Port-forwarding successful!");
			}

			else {
				Log("Unable to port forward.");
				Log("Double-check that UPnP is supported and enabled.");
			}

			Log("======= Done ========");
			MakeElementsInteractable(true);
		}

		private async void Button_CheckNATType_Click(object sender, EventArgs e) {
			if (string.IsNullOrWhiteSpace(Input_StunServer.Text)) {
				Log("No STUN server specified.");
				return;
			}

			MakeElementsInteractable(false);
			try {
				Log($"Trying to resolving host name: '{Input_StunServer.Text}' ...");
				if (!TryResolveHostName(Input_StunServer.Text, out IPAddress stun_PrimaryIPAddress)) {
					Log("Failed to resolve host name! Double-check host name and DNS settings. Perhaps try a different Server.");
					return;
				}

				Log($"STUN server IP obtained: {stun_PrimaryIPAddress}. Sending request..");

				IPEndPoint primaryStunServer = new IPEndPoint(stun_PrimaryIPAddress, (int)Input_StunServer_Port.Value);
				using (StunClient stunClient = new StunClient(primaryStunServer, (int)Input_ConnectionAttempts.Value, (double)Input_TimeOut.Value, Log)) {
					await stunClient.TryQueryIncomingNATType();

					Text_NAT_Type.Text = stunClient.NATType.ToString();

					if (stunClient.NATType == STUN_NetType.UDP_blocked) {
						return;
					}

					publicIPString = stunClient.publicIPAddress.ToString();
					TextBox_PublicIP.Text = publicIPString;

					OutboundBehaviorTest test1 = stunClient.IncomingQueryTest;

					if (test1 == null) {
						test1 = await stunClient.ConductBehaviorTest(primaryStunServer);
					}

					Log(test1.ToString());

					string hostName = Input_StunServer_2.Text;
					if (!TryResolveHostName(hostName, out IPAddress stun_alt)) {
						Log("Failed to resolve host name! Double-check host name and DNS settings. Perhaps try a different Server.");
						return;
					}

					IPEndPoint altStunServer = new IPEndPoint(stun_alt, (int)Input_StunServer2_Port.Value);
					OutboundBehaviorTest test2 = await stunClient.ConductBehaviorTest(altStunServer);

					Log(test2.ToString());

					//analyze behavior tests
					bool predictable = OutboundBehaviorTest.OutBoundBehaviorIsPredictable(test1, test2);
					Log(predictable
						? "External endpoints match internal endpoint and stay consistent after sending packets to different IP. - Hole-punching should work!"
						: "External endpoints are inconsistent.");
				}
			}
			catch (Exception ex) {
				Log($"Error: ({ex.GetType().Name}) {ex.Message}");
			}
			finally {
				MakeElementsInteractable(true);
				Log("======= Done ========");
			}
		}

		private void MakeElementsInteractable(bool active) {
			Cursor = active ? Cursors.Default : Cursors.WaitCursor;
			Button_CheckNATType.Enabled = active;
			Button_PortForward.Enabled = active;
			Button_HolePunch.Enabled = active;

			Input_ConnectionAttempts.Enabled = active;
			Input_PortToFree.Enabled = active;
			Input_StunServer.Enabled = active;
			Input_StunServer_2.Enabled = active;
			Input_TimeOut.Enabled = active;
			Input_StunServer_Port.Enabled = active;
			Input_StunServer2_Port.Enabled = active;
			Input_HolePunch_Address.Enabled = active;
			Input_HolePunch_Port.Enabled = active;
		}

		private async void Button_HolePunch_Click(object sender, EventArgs e) {
			var button = (Button)sender;

			if (holePunchInProgress) {
				holePunchInProgress = false;
				button.Text = "UDP Hole Punch";
				MakeElementsInteractable(true);
			}
			else {
				if (string.IsNullOrWhiteSpace(Input_HolePunch_Address.Text)) {
					Log("Enter a valid peer IP Address.");
					return;
				}

				int peerPort = (int)Input_HolePunch_Port.Value;
				if (!IPAddress.TryParse(Input_HolePunch_Address.Text, out IPAddress peerIP)) {
					Log("Invalid Peer IP.");
					return;
				}

				holePunchInProgress = true;
				MakeElementsInteractable(false);
				button.Enabled = true;
				button.Text = "Stop";

				var task = StartHolePunch(Log, (float)Input_TimeOut.Value, new IPEndPoint(peerIP, peerPort));
				await task;
			}
		}


		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent() {
			this.components = new System.ComponentModel.Container();
			this.Label_PortToFree = new System.Windows.Forms.Label();
			this.Input_PortToFree = new System.Windows.Forms.NumericUpDown();
			this.Label_STUN_Server = new System.Windows.Forms.Label();
			this.Input_StunServer = new System.Windows.Forms.TextBox();
			this.Input_StunServer_Port = new System.Windows.Forms.NumericUpDown();
			this.TextBox_Output = new System.Windows.Forms.TextBox();
			this.Label_ConnectionAttempts = new System.Windows.Forms.Label();
			this.Input_ConnectionAttempts = new System.Windows.Forms.NumericUpDown();
			this.Label_TimeOut = new System.Windows.Forms.Label();
			this.Input_TimeOut = new System.Windows.Forms.NumericUpDown();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.Button_CheckNATType = new System.Windows.Forms.Button();
			this.Button_HolePunch = new System.Windows.Forms.Button();
			this.Label_PeerAddress = new System.Windows.Forms.Label();
			this.Input_HolePunch_Port = new System.Windows.Forms.NumericUpDown();
			this.Label_PublicIP = new System.Windows.Forms.Label();
			this.TextBox_PublicIP = new System.Windows.Forms.TextBox();
			this.Input_StunServer_2 = new System.Windows.Forms.TextBox();
			this.Input_StunServer2_Port = new System.Windows.Forms.NumericUpDown();
			this.Label_Payload = new System.Windows.Forms.Label();
			this.Input_Payload = new System.Windows.Forms.TextBox();
			this.Button_PortForward = new System.Windows.Forms.Button();
			this.Text_NAT_Type = new System.Windows.Forms.TextBox();
			this.Label_STUN_Server_secondary = new System.Windows.Forms.Label();
			this.Input_HolePunch_Address = new System.Windows.Forms.TextBox();
			this.panel1 = new System.Windows.Forms.Panel();
			this.Label_NAT_Type = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this.Input_PortToFree)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_StunServer_Port)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_ConnectionAttempts)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_TimeOut)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_HolePunch_Port)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_StunServer2_Port)).BeginInit();
			this.SuspendLayout();
			// 
			// Label_PortToFree
			// 
			this.Label_PortToFree.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Label_PortToFree.Location = new System.Drawing.Point(205, 163);
			this.Label_PortToFree.Name = "Label_PortToFree";
			this.Label_PortToFree.Size = new System.Drawing.Size(75, 20);
			this.Label_PortToFree.TabIndex = 0;
			this.Label_PortToFree.Text = "Listen Port";
			this.Label_PortToFree.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// Input_PortToFree
			// 
			this.Input_PortToFree.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_PortToFree.Location = new System.Drawing.Point(286, 165);
			this.Input_PortToFree.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
			this.Input_PortToFree.Name = "Input_PortToFree";
			this.Input_PortToFree.Size = new System.Drawing.Size(67, 20);
			this.Input_PortToFree.TabIndex = 1;
			this.Input_PortToFree.Value = new decimal(new int[] { 7777, 0, 0, 0 });
			// 
			// Label_STUN_Server
			// 
			this.Label_STUN_Server.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Label_STUN_Server.Location = new System.Drawing.Point(59, 35);
			this.Label_STUN_Server.Name = "Label_STUN_Server";
			this.Label_STUN_Server.Size = new System.Drawing.Size(80, 20);
			this.Label_STUN_Server.TabIndex = 2;
			this.Label_STUN_Server.Text = "STUN Server";
			this.Label_STUN_Server.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// Input_StunServer
			// 
			this.Input_StunServer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_StunServer.Location = new System.Drawing.Point(145, 35);
			this.Input_StunServer.Name = "Input_StunServer";
			this.Input_StunServer.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this.Input_StunServer.Size = new System.Drawing.Size(135, 20);
			this.Input_StunServer.TabIndex = 3;
			this.Input_StunServer.Text = "stun.stunprotocol.org";
			this.Input_StunServer.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
			this.toolTip1.SetToolTip(this.Input_StunServer, "Hostname/IP of a publicly hosted STUN Server.\r\nThese have to be publicly reachabl" + "e - and not be behind a NAT/Firewall.\r\nThere are a lot of public STUN Servers th" + "at can easily by found on the internet.");
			// 
			// Input_StunServer_Port
			// 
			this.Input_StunServer_Port.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_StunServer_Port.Location = new System.Drawing.Point(286, 34);
			this.Input_StunServer_Port.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
			this.Input_StunServer_Port.Name = "Input_StunServer_Port";
			this.Input_StunServer_Port.Size = new System.Drawing.Size(67, 20);
			this.Input_StunServer_Port.TabIndex = 4;
			this.toolTip1.SetToolTip(this.Input_StunServer_Port, "STUN Server port number - usually 3478, but might differ from server to server.");
			this.Input_StunServer_Port.Value = new decimal(new int[] { 3478, 0, 0, 0 });
			// 
			// TextBox_Output
			// 
			this.TextBox_Output.AcceptsReturn = true;
			this.TextBox_Output.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
			this.TextBox_Output.Location = new System.Drawing.Point(13, 268);
			this.TextBox_Output.Multiline = true;
			this.TextBox_Output.Name = "TextBox_Output";
			this.TextBox_Output.ReadOnly = true;
			this.TextBox_Output.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.TextBox_Output.Size = new System.Drawing.Size(365, 230);
			this.TextBox_Output.TabIndex = 3;
			// 
			// Label_ConnectionAttempts
			// 
			this.Label_ConnectionAttempts.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Label_ConnectionAttempts.Location = new System.Drawing.Point(30, 85);
			this.Label_ConnectionAttempts.Name = "Label_ConnectionAttempts";
			this.Label_ConnectionAttempts.Size = new System.Drawing.Size(109, 20);
			this.Label_ConnectionAttempts.TabIndex = 10;
			this.Label_ConnectionAttempts.Text = "Connection Attempts";
			this.Label_ConnectionAttempts.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// Input_ConnectionAttempts
			// 
			this.Input_ConnectionAttempts.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_ConnectionAttempts.Location = new System.Drawing.Point(145, 85);
			this.Input_ConnectionAttempts.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
			this.Input_ConnectionAttempts.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			this.Input_ConnectionAttempts.Name = "Input_ConnectionAttempts";
			this.Input_ConnectionAttempts.Size = new System.Drawing.Size(67, 20);
			this.Input_ConnectionAttempts.TabIndex = 11;
			this.Input_ConnectionAttempts.Value = new decimal(new int[] { 3, 0, 0, 0 });
			// 
			// Label_TimeOut
			// 
			this.Label_TimeOut.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Label_TimeOut.Location = new System.Drawing.Point(227, 85);
			this.Label_TimeOut.Name = "Label_TimeOut";
			this.Label_TimeOut.Size = new System.Drawing.Size(53, 20);
			this.Label_TimeOut.TabIndex = 12;
			this.Label_TimeOut.Text = "Time out";
			this.Label_TimeOut.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// Input_TimeOut
			// 
			this.Input_TimeOut.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_TimeOut.DecimalPlaces = 1;
			this.Input_TimeOut.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
			this.Input_TimeOut.Location = new System.Drawing.Point(286, 85);
			this.Input_TimeOut.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
			this.Input_TimeOut.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			this.Input_TimeOut.Name = "Input_TimeOut";
			this.Input_TimeOut.Size = new System.Drawing.Size(67, 20);
			this.Input_TimeOut.TabIndex = 13;
			this.Input_TimeOut.Value = new decimal(new int[] { 2, 0, 0, 0 });
			// 
			// Button_CheckNATType
			// 
			this.Button_CheckNATType.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Button_CheckNATType.Location = new System.Drawing.Point(253, 110);
			this.Button_CheckNATType.Name = "Button_CheckNATType";
			this.Button_CheckNATType.Size = new System.Drawing.Size(100, 20);
			this.Button_CheckNATType.TabIndex = 15;
			this.Button_CheckNATType.Text = "Check NAT Type";
			this.toolTip1.SetToolTip(this.Button_CheckNATType, "Attempts to determine NAT Type through using the STUN Protocol.");
			this.Button_CheckNATType.UseVisualStyleBackColor = true;
			this.Button_CheckNATType.Click += new System.EventHandler(this.Button_CheckNATType_Click);
			// 
			// Button_HolePunch
			// 
			this.Button_HolePunch.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Button_HolePunch.Location = new System.Drawing.Point(253, 242);
			this.Button_HolePunch.Name = "Button_HolePunch";
			this.Button_HolePunch.Size = new System.Drawing.Size(100, 20);
			this.Button_HolePunch.TabIndex = 16;
			this.Button_HolePunch.Text = "UDP Hole Punch";
			this.toolTip1.SetToolTip(this.Button_HolePunch, "Attempts to communicate with peer through UDP hole punching.\r\nRequires you to ent" + "er the peer\'s IP address and port. \r\n\r\nRequires your peer to run the UDP hole pu" + "nch protocol at the same time.\r\n\r\n");
			this.Button_HolePunch.UseVisualStyleBackColor = true;
			this.Button_HolePunch.Click += new System.EventHandler(this.Button_HolePunch_Click);
			// 
			// Label_PeerAddress
			// 
			this.Label_PeerAddress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Label_PeerAddress.Location = new System.Drawing.Point(99, 189);
			this.Label_PeerAddress.Name = "Label_PeerAddress";
			this.Label_PeerAddress.Size = new System.Drawing.Size(80, 20);
			this.Label_PeerAddress.TabIndex = 17;
			this.Label_PeerAddress.Text = "Peer Address";
			this.Label_PeerAddress.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			this.toolTip1.SetToolTip(this.Label_PeerAddress, "Hostname/IP of a peer that is also currently attempting a holepunch session.");
			// 
			// Input_HolePunch_Port
			// 
			this.Input_HolePunch_Port.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_HolePunch_Port.Location = new System.Drawing.Point(286, 190);
			this.Input_HolePunch_Port.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
			this.Input_HolePunch_Port.Name = "Input_HolePunch_Port";
			this.Input_HolePunch_Port.Size = new System.Drawing.Size(67, 20);
			this.Input_HolePunch_Port.TabIndex = 19;
			this.toolTip1.SetToolTip(this.Input_HolePunch_Port, "Peer port number - usually 3478, but might differ from server to server.");
			this.Input_HolePunch_Port.Value = new decimal(new int[] { 7777, 0, 0, 0 });
			// 
			// Label_PublicIP
			// 
			this.Label_PublicIP.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Label_PublicIP.Location = new System.Drawing.Point(167, 8);
			this.Label_PublicIP.Name = "Label_PublicIP";
			this.Label_PublicIP.Size = new System.Drawing.Size(80, 20);
			this.Label_PublicIP.TabIndex = 23;
			this.Label_PublicIP.Text = "Public IP";
			this.Label_PublicIP.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			this.toolTip1.SetToolTip(this.Label_PublicIP, "Public IP\r\n");
			// 
			// TextBox_PublicIP
			// 
			this.TextBox_PublicIP.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.TextBox_PublicIP.Location = new System.Drawing.Point(253, 8);
			this.TextBox_PublicIP.Name = "TextBox_PublicIP";
			this.TextBox_PublicIP.ReadOnly = true;
			this.TextBox_PublicIP.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this.TextBox_PublicIP.Size = new System.Drawing.Size(100, 20);
			this.TextBox_PublicIP.TabIndex = 24;
			this.TextBox_PublicIP.Text = "unknown";
			this.toolTip1.SetToolTip(this.TextBox_PublicIP, "Public IP Address.");
			// 
			// Input_StunServer_2
			// 
			this.Input_StunServer_2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_StunServer_2.Location = new System.Drawing.Point(145, 60);
			this.Input_StunServer_2.Name = "Input_StunServer_2";
			this.Input_StunServer_2.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this.Input_StunServer_2.Size = new System.Drawing.Size(135, 20);
			this.Input_StunServer_2.TabIndex = 26;
			this.Input_StunServer_2.Text = "stun.dls.net";
			this.Input_StunServer_2.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
			this.toolTip1.SetToolTip(this.Input_StunServer_2, "Hostname/IP of a publicly hosted STUN Server.\r\nThese have to be publicly reachabl" + "e - and not be behind a NAT/Firewall.\r\nThere are a lot of public STUN Servers th" + "at can easily by found on the internet.");
			// 
			// Input_StunServer2_Port
			// 
			this.Input_StunServer2_Port.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_StunServer2_Port.Location = new System.Drawing.Point(286, 60);
			this.Input_StunServer2_Port.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
			this.Input_StunServer2_Port.Name = "Input_StunServer2_Port";
			this.Input_StunServer2_Port.Size = new System.Drawing.Size(67, 20);
			this.Input_StunServer2_Port.TabIndex = 27;
			this.toolTip1.SetToolTip(this.Input_StunServer2_Port, "STUN Server port number - usually 3478, but might differ from server to server.");
			this.Input_StunServer2_Port.Value = new decimal(new int[] { 3478, 0, 0, 0 });
			// 
			// Label_Payload
			// 
			this.Label_Payload.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Label_Payload.Location = new System.Drawing.Point(99, 216);
			this.Label_Payload.Name = "Label_Payload";
			this.Label_Payload.Size = new System.Drawing.Size(80, 20);
			this.Label_Payload.TabIndex = 30;
			this.Label_Payload.Text = "Payload";
			this.Label_Payload.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			this.toolTip1.SetToolTip(this.Label_Payload, "String that get\'s sent to peer.");
			// 
			// Input_Payload
			// 
			this.Input_Payload.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_Payload.Location = new System.Drawing.Point(185, 216);
			this.Input_Payload.MaxLength = 100;
			this.Input_Payload.Name = "Input_Payload";
			this.Input_Payload.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this.Input_Payload.Size = new System.Drawing.Size(168, 20);
			this.Input_Payload.TabIndex = 29;
			this.Input_Payload.Text = "Hello!";
			this.Input_Payload.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
			this.toolTip1.SetToolTip(this.Input_Payload, "String that will be sent to peer.");
			// 
			// Button_PortForward
			// 
			this.Button_PortForward.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Button_PortForward.Location = new System.Drawing.Point(99, 163);
			this.Button_PortForward.Name = "Button_PortForward";
			this.Button_PortForward.Size = new System.Drawing.Size(122, 20);
			this.Button_PortForward.TabIndex = 33;
			this.Button_PortForward.Text = "Port Forward (UPnP)";
			this.toolTip1.SetToolTip(this.Button_PortForward, "Attempts to port-forward using UPnP (has to be enabled/supported on NAT device/ro" + "uter).");
			this.Button_PortForward.UseVisualStyleBackColor = true;
			this.Button_PortForward.Click += new System.EventHandler(this.Button_PortForward_Click);
			// 
			// Text_NAT_Type
			// 
			this.Text_NAT_Type.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Text_NAT_Type.Location = new System.Drawing.Point(145, 110);
			this.Text_NAT_Type.Name = "Text_NAT_Type";
			this.Text_NAT_Type.ReadOnly = true;
			this.Text_NAT_Type.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this.Text_NAT_Type.Size = new System.Drawing.Size(102, 20);
			this.Text_NAT_Type.TabIndex = 32;
			this.Text_NAT_Type.Text = "unknown";
			// 
			// Label_STUN_Server_secondary
			// 
			this.Label_STUN_Server_secondary.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Label_STUN_Server_secondary.Location = new System.Drawing.Point(40, 60);
			this.Label_STUN_Server_secondary.Name = "Label_STUN_Server_secondary";
			this.Label_STUN_Server_secondary.Size = new System.Drawing.Size(99, 20);
			this.Label_STUN_Server_secondary.TabIndex = 25;
			this.Label_STUN_Server_secondary.Text = "STUN Server (alt)";
			this.Label_STUN_Server_secondary.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// Input_HolePunch_Address
			// 
			this.Input_HolePunch_Address.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Input_HolePunch_Address.Location = new System.Drawing.Point(185, 190);
			this.Input_HolePunch_Address.Name = "Input_HolePunch_Address";
			this.Input_HolePunch_Address.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this.Input_HolePunch_Address.Size = new System.Drawing.Size(95, 20);
			this.Input_HolePunch_Address.TabIndex = 18;
			this.Input_HolePunch_Address.Text = "0.0.0.0";
			this.Input_HolePunch_Address.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
			// 
			// panel1
			// 
			this.panel1.BackColor = System.Drawing.SystemColors.ControlText;
			this.panel1.Location = new System.Drawing.Point(13, 141);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(365, 1);
			this.panel1.TabIndex = 28;
			// 
			// Label_NAT_Type
			// 
			this.Label_NAT_Type.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.Label_NAT_Type.Location = new System.Drawing.Point(30, 110);
			this.Label_NAT_Type.Name = "Label_NAT_Type";
			this.Label_NAT_Type.Size = new System.Drawing.Size(109, 20);
			this.Label_NAT_Type.TabIndex = 31;
			this.Label_NAT_Type.Text = "NAT Type";
			this.Label_NAT_Type.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// winFormMain
			// 
			this.ClientSize = new System.Drawing.Size(390, 506);
			this.Controls.Add(this.Button_PortForward);
			this.Controls.Add(this.Text_NAT_Type);
			this.Controls.Add(this.Label_NAT_Type);
			this.Controls.Add(this.Label_Payload);
			this.Controls.Add(this.Input_Payload);
			this.Controls.Add(this.panel1);
			this.Controls.Add(this.Input_StunServer2_Port);
			this.Controls.Add(this.Input_StunServer_2);
			this.Controls.Add(this.Label_STUN_Server_secondary);
			this.Controls.Add(this.TextBox_PublicIP);
			this.Controls.Add(this.Label_PublicIP);
			this.Controls.Add(this.Input_HolePunch_Port);
			this.Controls.Add(this.Input_HolePunch_Address);
			this.Controls.Add(this.Label_PeerAddress);
			this.Controls.Add(this.Button_HolePunch);
			this.Controls.Add(this.Button_CheckNATType);
			this.Controls.Add(this.Input_TimeOut);
			this.Controls.Add(this.Label_TimeOut);
			this.Controls.Add(this.Input_ConnectionAttempts);
			this.Controls.Add(this.Label_ConnectionAttempts);
			this.Controls.Add(this.Input_StunServer_Port);
			this.Controls.Add(this.Label_STUN_Server);
			this.Controls.Add(this.TextBox_Output);
			this.Controls.Add(this.Input_StunServer);
			this.Controls.Add(this.Label_PortToFree);
			this.Controls.Add(this.Input_PortToFree);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Fixed3D;
			this.MinimumSize = new System.Drawing.Size(400, 500);
			this.Name = "winFormMain";
			this.ShowIcon = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "StunMage";
			((System.ComponentModel.ISupportInitialize)(this.Input_PortToFree)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_StunServer_Port)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_ConnectionAttempts)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_TimeOut)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_HolePunch_Port)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.Input_StunServer2_Port)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		private System.Windows.Forms.Label Label_NAT_Type;
		private System.Windows.Forms.TextBox Text_NAT_Type;

		private System.Windows.Forms.TextBox Input_Payload;

		private System.Windows.Forms.Label Label_Payload;

		private System.Windows.Forms.Panel panel1;

		private System.Windows.Forms.NumericUpDown Input_StunServer2_Port;

		private System.Windows.Forms.Label Label_STUN_Server_secondary;
		private System.Windows.Forms.TextBox Input_StunServer_2;
	}
}