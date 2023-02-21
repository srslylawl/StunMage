using System;
using System.ComponentModel;
using System.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;

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
        private Label Label_PeerAddress;
        private System.Windows.Forms.Label Label_Outbound_Behavior;
        private System.Windows.Forms.TextBox Input_HolePunch_Address;
        private System.Windows.Forms.NumericUpDown Input_HolePunch_Port;
        private System.Windows.Forms.TextBox TextBox_Output;
        private System.Windows.Forms.TextBox Text_Outbound_Behavior;

        private System.Windows.Forms.Label Label_PublicIP;
        private System.Windows.Forms.TextBox TextBox_PublicIP;

        private Label Label_IncomingGrp;
        private TextBox Text_IncomingGrp;
        private Label Label_OutgoingGrp;
        private TextBox Text_OutgoingGrp;
        private Label Label_OutgoingGrpRemote;
        private Label Label_IncomingGrpRemote;
        private ComboBox Combo_IncomingGrp;
        private ComboBox Combo_OutgoingGrp;
        private Label label1;
        private TextBox Text_Technique_Required;
        private Label label2;
        private TextBox Text_ExtraCondition;
        private Label label3;
        private TextBox Text_RecommendedRole;
        private Panel panel2;
        private Panel panel3;
        private Panel panel1;
        private Label label4;
        private Label label5;
        private CheckBox CheckBox_RequirePassword;
        private TextBox Text_QueriedPort;
        private Button Button_QueryPort;
        private Button Button_HolePunchQueried;
        private StunMage stunMageClient = new StunMage();


        private Socket queriedSocket;
        private CheckBox CheckBox_PreferListenPort;

        public winFormMain() {
            InitializeComponent();
            stunMageClient.LogFunctionVerbose += Log;

            Combo_IncomingGrp.DataSource = Enum.GetValues(typeof(IncomingBehaviorGroup));
            Combo_OutgoingGrp.DataSource = Enum.GetValues(typeof(OutgoingBehaviorGroup));

            GetStunServerSettings(1, "stun.dls.net", "3478");
            GetStunServerSettings(2, "stun.gmx.de", "3478");
        }

        private void GetStunServerSettings(int id, string fallbackServer, string fallbackPort) {
            string stunServer = ConfigurationManager.AppSettings[$"StunServer{id}"];
            string stunPort = ConfigurationManager.AppSettings[$"StunPort{id}"];
            bool noServer = string.IsNullOrEmpty(stunServer);
            if(noServer) {
                stunServer = fallbackServer;
                stunPort = fallbackPort;
            }
            bool noPort = string.IsNullOrEmpty(stunPort);
            if(noPort) {
                stunPort = fallbackPort;
            }

            var serverTextField = id == 1 ? Input_StunServer : Input_StunServer_2;
            var serverPortField = id == 2 ? Input_StunServer_Port : Input_StunServer2_Port;
            serverTextField.Text = stunServer;
            serverPortField.Text = stunPort;
        }

        private void Log(string str) {
            TextBox_Output.AppendText($"{str}\r\n");
        }

        private async void Button_PortForward_Click(object sender, EventArgs e) {
            MakeElementsInteractable(false);
            ushort port = (ushort)Convert.ToInt32(Input_PortToFree.Value);
            bool forwarded = await stunMageClient.TryPortForwarding(port);
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

            //Query first

            if (!stunMageClient.TryResolveHostName(Input_StunServer.Text, out var iPAddress_1)) {
                return;
            }

            if (!stunMageClient.TryResolveHostName(Input_StunServer_2.Text, out var iPAddress_2)) {
                return;
            }

            MakeElementsInteractable(false);
            try {
                await stunMageClient.CheckNATType(new IPEndPoint(iPAddress_1, (int)Input_StunServer_Port.Value), new IPEndPoint(iPAddress_2, (int)Input_StunServer2_Port.Value));
                Text_NAT_Type.Text = stunMageClient.NATType.ToString();
                TextBox_PublicIP.Text = stunMageClient.PublicIPString;
                Text_IncomingGrp.Text = stunMageClient.EndPointBehaviorTuple.IncomingBehaviorGroup.ToString();
                Text_OutgoingGrp.Text = stunMageClient.EndPointBehaviorTuple.OutgoingBehaviorGroup.ToString();
                Text_Outbound_Behavior.Text = stunMageClient.OutboundBehaviorType.ToString();
                EvaluateConnectionCompatibility();
                SaveServerSettings("StunServer1", Input_StunServer.Text);
                SaveServerSettings("StunServer2", Input_StunServer_2.Text);
                SaveServerSettings("StunPort1", Input_StunServer_Port.Text);
                SaveServerSettings("StunPort2", Input_StunServer2_Port.Text);
            }
            catch (Exception ex) {
                Log($"Error: ({ex.GetType().Name}) {ex.Message}");
            }
            finally {
                MakeElementsInteractable(true);
            }
        }


        private void EvaluateConnectionCompatibility() {
            if (string.IsNullOrEmpty(stunMageClient.PublicIPString)) {
                return;
            }

            var tuple_this = stunMageClient.EndPointBehaviorTuple;
            var tuple_other = new EndPointBehaviorTuple() {
                IncomingBehaviorGroup = (IncomingBehaviorGroup)Combo_IncomingGrp.SelectedItem,
                OutgoingBehaviorGroup = (OutgoingBehaviorGroup)Combo_OutgoingGrp.SelectedItem
            };

            var res = tuple_this.GetBestResult(tuple_other);

            var bestEvaluation = res.BestEvaluation;
            var techniqueRequired = bestEvaluation.ConnectionTechnique;
            var condition = bestEvaluation.ConnectionCondition;
            var bestRole = res.BestRoleForClient;

            Text_Technique_Required.Text = techniqueRequired.ToString();
            Text_ExtraCondition.Text = condition.ToString();
            Text_RecommendedRole.Text = bestRole.ToString();
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
            Combo_IncomingGrp.Enabled = active;
            Combo_OutgoingGrp.Enabled = active;
            Button_QueryPort.Enabled = active;
        }



        private void Button_HolePunch_Click(object sender, EventArgs e) {
            var button = (Button)sender;

            if (stunMageClient.HolePunchInProgress) {
                stunMageClient.StopHolePunch();
                button.Text = "Start Hole Punch\n(using Listen Port)";
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

                MakeElementsInteractable(false);
                button.Enabled = true;
                button.Text = "Stop";

                stunMageClient.StartHolePunch((ushort)Input_PortToFree.Value, new IPEndPoint(peerIP, peerPort), endpoint => {
                    Log($"Hole-Punching was successful! Remote endpoint: {endpoint}");
                    button.Text = "Start Hole Punch\n(using Listen Port)";
                    MakeElementsInteractable(true);
                });
            }
        }

        private void Button_HolePunchQueried_Click(object sender, EventArgs e) {
            var button = (Button)sender;

            if (stunMageClient.HolePunchInProgress) {
                stunMageClient.StopHolePunch();
                button.Text = "Start Hole Punch\n(using Queried Port)";
                MakeElementsInteractable(true);
                //DisposeQueriedSocket();
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

                MakeElementsInteractable(false);
                button.Enabled = true;
                button.Text = "Stop";

                stunMageClient.StartHolePunch(queriedSocket, new IPEndPoint(peerIP, peerPort), endpoint => {
                    Log($"Hole-Punching was successful! Remote endpoint: {endpoint}");
                    button.Text = "Start Hole Punch\n(using Queried Port)";
                    MakeElementsInteractable(true);
                    DisposeQueriedSocket();
                });
            }
        }


        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.Label Label_Remote;
            System.Windows.Forms.Label Label_Result;
            System.Windows.Forms.Label Local;
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
            this.Input_Password = new System.Windows.Forms.TextBox();
            this.Button_PortForward = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.Button_HolePunchQueried = new System.Windows.Forms.Button();
            this.Button_QueryPort = new System.Windows.Forms.Button();
            this.Text_QueriedPort = new System.Windows.Forms.TextBox();
            this.CheckBox_PreferListenPort = new System.Windows.Forms.CheckBox();
            this.Text_NAT_Type = new System.Windows.Forms.TextBox();
            this.Label_STUN_Server_secondary = new System.Windows.Forms.Label();
            this.Input_HolePunch_Address = new System.Windows.Forms.TextBox();
            this.Label_NAT_Type = new System.Windows.Forms.Label();
            this.Label_Outbound_Behavior = new System.Windows.Forms.Label();
            this.Text_Outbound_Behavior = new System.Windows.Forms.TextBox();
            this.Label_IncomingGrp = new System.Windows.Forms.Label();
            this.Text_IncomingGrp = new System.Windows.Forms.TextBox();
            this.Label_OutgoingGrp = new System.Windows.Forms.Label();
            this.Text_OutgoingGrp = new System.Windows.Forms.TextBox();
            this.Label_OutgoingGrpRemote = new System.Windows.Forms.Label();
            this.Label_IncomingGrpRemote = new System.Windows.Forms.Label();
            this.Combo_IncomingGrp = new System.Windows.Forms.ComboBox();
            this.Combo_OutgoingGrp = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.Text_Technique_Required = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.Text_ExtraCondition = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.Text_RecommendedRole = new System.Windows.Forms.TextBox();
            this.panel2 = new System.Windows.Forms.Panel();
            this.CheckBox_RequirePassword = new System.Windows.Forms.CheckBox();
            this.panel3 = new System.Windows.Forms.Panel();
            this.panel1 = new System.Windows.Forms.Panel();
            Label_Remote = new System.Windows.Forms.Label();
            Label_Result = new System.Windows.Forms.Label();
            Local = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.Input_PortToFree)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.Input_StunServer_Port)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.Input_ConnectionAttempts)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.Input_TimeOut)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.Input_HolePunch_Port)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.Input_StunServer2_Port)).BeginInit();
            this.panel2.SuspendLayout();
            this.panel3.SuspendLayout();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // Label_Remote
            // 
            Label_Remote.Location = new System.Drawing.Point(118, 72);
            Label_Remote.Name = "Label_Remote";
            Label_Remote.Size = new System.Drawing.Size(171, 20);
            Label_Remote.TabIndex = 40;
            Label_Remote.Text = "Select Remote Behavior Type";
            Label_Remote.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Label_Result
            // 
            Label_Result.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            Label_Result.Location = new System.Drawing.Point(268, 204);
            Label_Result.Name = "Label_Result";
            Label_Result.Size = new System.Drawing.Size(149, 20);
            Label_Result.TabIndex = 49;
            Label_Result.Text = "Connection Compatibility";
            Label_Result.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // Local
            // 
            Local.Location = new System.Drawing.Point(158, 0);
            Local.Name = "Local";
            Local.Size = new System.Drawing.Size(109, 20);
            Local.TabIndex = 49;
            Local.Text = "Local";
            Local.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Label_PortToFree
            // 
            this.Label_PortToFree.Location = new System.Drawing.Point(143, 8);
            this.Label_PortToFree.Name = "Label_PortToFree";
            this.Label_PortToFree.Size = new System.Drawing.Size(63, 20);
            this.Label_PortToFree.TabIndex = 0;
            this.Label_PortToFree.Text = "Listen Port";
            this.Label_PortToFree.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Input_PortToFree
            // 
            this.Input_PortToFree.Location = new System.Drawing.Point(212, 10);
            this.Input_PortToFree.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.Input_PortToFree.Name = "Input_PortToFree";
            this.Input_PortToFree.Size = new System.Drawing.Size(67, 20);
            this.Input_PortToFree.TabIndex = 1;
            this.toolTip1.SetToolTip(this.Input_PortToFree, "Incoming port - needs to match peer\'s peer address port.");
            this.Input_PortToFree.Value = new decimal(new int[] {
            7777,
            0,
            0,
            0});
            // 
            // Label_STUN_Server
            // 
            this.Label_STUN_Server.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Label_STUN_Server.Location = new System.Drawing.Point(35, 31);
            this.Label_STUN_Server.Name = "Label_STUN_Server";
            this.Label_STUN_Server.Size = new System.Drawing.Size(80, 20);
            this.Label_STUN_Server.TabIndex = 2;
            this.Label_STUN_Server.Text = "STUN Server";
            this.Label_STUN_Server.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Input_StunServer
            // 
            this.Input_StunServer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Input_StunServer.Location = new System.Drawing.Point(121, 31);
            this.Input_StunServer.Name = "Input_StunServer";
            this.Input_StunServer.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Input_StunServer.Size = new System.Drawing.Size(135, 20);
            this.Input_StunServer.TabIndex = 3;
            this.Input_StunServer.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.toolTip1.SetToolTip(this.Input_StunServer, "Hostname/IP of a publicly hosted STUN Server.\r\nThese have to be publicly reachabl" +
        "e - and not be behind a NAT/Firewall.\r\nThere are a lot of public STUN Servers th" +
        "at can easily by found on the internet.");
            // 
            // Input_StunServer_Port
            // 
            this.Input_StunServer_Port.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Input_StunServer_Port.Location = new System.Drawing.Point(262, 30);
            this.Input_StunServer_Port.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.Input_StunServer_Port.Name = "Input_StunServer_Port";
            this.Input_StunServer_Port.Size = new System.Drawing.Size(67, 20);
            this.Input_StunServer_Port.TabIndex = 4;
            this.toolTip1.SetToolTip(this.Input_StunServer_Port, "STUN Server port number - usually 3478, but might differ from server to server.");
            this.Input_StunServer_Port.Value = new decimal(new int[] {
            3478,
            0,
            0,
            0});
            // 
            // TextBox_Output
            // 
            this.TextBox_Output.AcceptsReturn = true;
            this.TextBox_Output.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TextBox_Output.Location = new System.Drawing.Point(13, 438);
            this.TextBox_Output.Multiline = true;
            this.TextBox_Output.Name = "TextBox_Output";
            this.TextBox_Output.ReadOnly = true;
            this.TextBox_Output.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.TextBox_Output.Size = new System.Drawing.Size(635, 270);
            this.TextBox_Output.TabIndex = 3;
            // 
            // Label_ConnectionAttempts
            // 
            this.Label_ConnectionAttempts.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Label_ConnectionAttempts.Location = new System.Drawing.Point(6, 81);
            this.Label_ConnectionAttempts.Name = "Label_ConnectionAttempts";
            this.Label_ConnectionAttempts.Size = new System.Drawing.Size(109, 20);
            this.Label_ConnectionAttempts.TabIndex = 10;
            this.Label_ConnectionAttempts.Text = "Connection Attempts";
            this.Label_ConnectionAttempts.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Input_ConnectionAttempts
            // 
            this.Input_ConnectionAttempts.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Input_ConnectionAttempts.Location = new System.Drawing.Point(121, 81);
            this.Input_ConnectionAttempts.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.Input_ConnectionAttempts.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.Input_ConnectionAttempts.Name = "Input_ConnectionAttempts";
            this.Input_ConnectionAttempts.Size = new System.Drawing.Size(67, 20);
            this.Input_ConnectionAttempts.TabIndex = 11;
            this.Input_ConnectionAttempts.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            // 
            // Label_TimeOut
            // 
            this.Label_TimeOut.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Label_TimeOut.Location = new System.Drawing.Point(203, 81);
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
            this.Input_TimeOut.Increment = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            this.Input_TimeOut.Location = new System.Drawing.Point(262, 81);
            this.Input_TimeOut.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.Input_TimeOut.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.Input_TimeOut.Name = "Input_TimeOut";
            this.Input_TimeOut.Size = new System.Drawing.Size(67, 20);
            this.Input_TimeOut.TabIndex = 13;
            this.Input_TimeOut.Value = new decimal(new int[] {
            2,
            0,
            0,
            0});
            // 
            // Button_CheckNATType
            // 
            this.Button_CheckNATType.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Button_CheckNATType.Location = new System.Drawing.Point(262, 106);
            this.Button_CheckNATType.Name = "Button_CheckNATType";
            this.Button_CheckNATType.Size = new System.Drawing.Size(67, 45);
            this.Button_CheckNATType.TabIndex = 15;
            this.Button_CheckNATType.Text = "Check";
            this.toolTip1.SetToolTip(this.Button_CheckNATType, "Attempts to determine NAT Type through using the STUN Protocol.");
            this.Button_CheckNATType.UseVisualStyleBackColor = true;
            this.Button_CheckNATType.Click += new System.EventHandler(this.Button_CheckNATType_Click);
            // 
            // Button_HolePunch
            // 
            this.Button_HolePunch.Location = new System.Drawing.Point(188, 123);
            this.Button_HolePunch.Name = "Button_HolePunch";
            this.Button_HolePunch.Size = new System.Drawing.Size(100, 39);
            this.Button_HolePunch.TabIndex = 16;
            this.Button_HolePunch.Text = "Start Hole Punch\r\n(using Listen Port)";
            this.toolTip1.SetToolTip(this.Button_HolePunch, "Attempts to communicate with peer through UDP hole punching.\r\nRequires you to ent" +
        "er the peer\'s IP address and port. \r\n\r\nRequires your peer to run the UDP hole pu" +
        "nch protocol at the same time.\r\n\r\n");
            this.Button_HolePunch.UseVisualStyleBackColor = true;
            this.Button_HolePunch.Click += new System.EventHandler(this.Button_HolePunch_Click);
            // 
            // Label_PeerAddress
            // 
            this.Label_PeerAddress.Location = new System.Drawing.Point(25, 34);
            this.Label_PeerAddress.Name = "Label_PeerAddress";
            this.Label_PeerAddress.Size = new System.Drawing.Size(80, 20);
            this.Label_PeerAddress.TabIndex = 17;
            this.Label_PeerAddress.Text = "Peer Address";
            this.Label_PeerAddress.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.toolTip1.SetToolTip(this.Label_PeerAddress, "Hostname/IP of a peer that is also currently attempting a holepunch session.");
            // 
            // Input_HolePunch_Port
            // 
            this.Input_HolePunch_Port.Location = new System.Drawing.Point(212, 35);
            this.Input_HolePunch_Port.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.Input_HolePunch_Port.Name = "Input_HolePunch_Port";
            this.Input_HolePunch_Port.Size = new System.Drawing.Size(67, 20);
            this.Input_HolePunch_Port.TabIndex = 19;
            this.toolTip1.SetToolTip(this.Input_HolePunch_Port, "Peer port number - needs to be peer\'s \"listen port\"");
            this.Input_HolePunch_Port.Value = new decimal(new int[] {
            7777,
            0,
            0,
            0});
            // 
            // Label_PublicIP
            // 
            this.Label_PublicIP.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Label_PublicIP.Location = new System.Drawing.Point(143, 4);
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
            this.TextBox_PublicIP.Location = new System.Drawing.Point(229, 4);
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
            this.Input_StunServer_2.Location = new System.Drawing.Point(121, 56);
            this.Input_StunServer_2.Name = "Input_StunServer_2";
            this.Input_StunServer_2.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Input_StunServer_2.Size = new System.Drawing.Size(135, 20);
            this.Input_StunServer_2.TabIndex = 26;
            this.Input_StunServer_2.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.toolTip1.SetToolTip(this.Input_StunServer_2, "Hostname/IP of a publicly hosted STUN Server.\r\nThese have to be publicly reachabl" +
        "e - and not be behind a NAT/Firewall.\r\nThere are a lot of public STUN Servers th" +
        "at can easily by found on the internet.");
            // 
            // Input_StunServer2_Port
            // 
            this.Input_StunServer2_Port.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Input_StunServer2_Port.Location = new System.Drawing.Point(262, 56);
            this.Input_StunServer2_Port.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.Input_StunServer2_Port.Name = "Input_StunServer2_Port";
            this.Input_StunServer2_Port.Size = new System.Drawing.Size(67, 20);
            this.Input_StunServer2_Port.TabIndex = 27;
            this.toolTip1.SetToolTip(this.Input_StunServer2_Port, "STUN Server port number - usually 3478, but might differ from server to server.");
            this.Input_StunServer2_Port.Value = new decimal(new int[] {
            3478,
            0,
            0,
            0});
            // 
            // Input_Password
            // 
            this.Input_Password.Enabled = false;
            this.Input_Password.Location = new System.Drawing.Point(133, 61);
            this.Input_Password.MaxLength = 100;
            this.Input_Password.Name = "Input_Password";
            this.Input_Password.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Input_Password.Size = new System.Drawing.Size(146, 20);
            this.Input_Password.TabIndex = 29;
            this.Input_Password.Text = "Hello!";
            this.Input_Password.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.toolTip1.SetToolTip(this.Input_Password, "String that will be sent to peer.");
            // 
            // Button_PortForward
            // 
            this.Button_PortForward.Location = new System.Drawing.Point(7, 8);
            this.Button_PortForward.Name = "Button_PortForward";
            this.Button_PortForward.Size = new System.Drawing.Size(135, 20);
            this.Button_PortForward.TabIndex = 33;
            this.Button_PortForward.Text = "Port Forward (UPnP)";
            this.toolTip1.SetToolTip(this.Button_PortForward, "Attempts to port-forward using UPnP (has to be enabled/supported on NAT device/ro" +
        "uter).");
            this.Button_PortForward.UseVisualStyleBackColor = true;
            this.Button_PortForward.Click += new System.EventHandler(this.Button_PortForward_Click);
            // 
            // label4
            // 
            this.label4.BackColor = System.Drawing.SystemColors.Control;
            this.label4.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(121, 9);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(116, 20);
            this.label4.TabIndex = 36;
            this.label4.Text = "Query NAT Type";
            this.label4.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.toolTip1.SetToolTip(this.label4, "Public IP\r\n");
            // 
            // label5
            // 
            this.label5.BackColor = System.Drawing.SystemColors.Control;
            this.label5.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.label5.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label5.Location = new System.Drawing.Point(420, 9);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(152, 20);
            this.label5.TabIndex = 59;
            this.label5.Text = "UDP Hole Punching";
            this.label5.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.toolTip1.SetToolTip(this.label5, "Public IP\r\n");
            // 
            // Button_HolePunchQueried
            // 
            this.Button_HolePunchQueried.Enabled = false;
            this.Button_HolePunchQueried.Location = new System.Drawing.Point(7, 122);
            this.Button_HolePunchQueried.Name = "Button_HolePunchQueried";
            this.Button_HolePunchQueried.Size = new System.Drawing.Size(109, 39);
            this.Button_HolePunchQueried.TabIndex = 39;
            this.Button_HolePunchQueried.Text = "Start Hole Punch\r\n(using Queried Port)";
            this.toolTip1.SetToolTip(this.Button_HolePunchQueried, "Attempts to communicate with peer through UDP hole punching.\r\nRequires you to ent" +
        "er the peer\'s IP address and port. \r\n\r\nRequires your peer to run the UDP hole pu" +
        "nch protocol at the same time.\r\n\r\n");
            this.Button_HolePunchQueried.UseVisualStyleBackColor = true;
            this.Button_HolePunchQueried.Click += new System.EventHandler(this.Button_HolePunchQueried_Click);
            // 
            // Button_QueryPort
            // 
            this.Button_QueryPort.Location = new System.Drawing.Point(7, 85);
            this.Button_QueryPort.Name = "Button_QueryPort";
            this.Button_QueryPort.Size = new System.Drawing.Size(90, 35);
            this.Button_QueryPort.TabIndex = 40;
            this.Button_QueryPort.Text = "Bind Queried Port";
            this.toolTip1.SetToolTip(this.Button_QueryPort, "Binds socket to random or prefered endpoint and returns the queried external port" +
        ".");
            this.Button_QueryPort.UseVisualStyleBackColor = true;
            this.Button_QueryPort.Click += new System.EventHandler(this.Button_QueryPort_Click);
            // 
            // Text_QueriedPort
            // 
            this.Text_QueriedPort.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Text_QueriedPort.Location = new System.Drawing.Point(103, 87);
            this.Text_QueriedPort.Name = "Text_QueriedPort";
            this.Text_QueriedPort.ReadOnly = true;
            this.Text_QueriedPort.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Text_QueriedPort.Size = new System.Drawing.Size(67, 20);
            this.Text_QueriedPort.TabIndex = 36;
            this.Text_QueriedPort.Text = "unknown";
            this.toolTip1.SetToolTip(this.Text_QueriedPort, "The queried port number");
            // 
            // CheckBox_PreferListenPort
            // 
            this.CheckBox_PreferListenPort.AutoSize = true;
            this.CheckBox_PreferListenPort.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.CheckBox_PreferListenPort.Location = new System.Drawing.Point(176, 89);
            this.CheckBox_PreferListenPort.Name = "CheckBox_PreferListenPort";
            this.CheckBox_PreferListenPort.Size = new System.Drawing.Size(107, 17);
            this.CheckBox_PreferListenPort.TabIndex = 41;
            this.CheckBox_PreferListenPort.Text = "Prefer Listen Port";
            this.toolTip1.SetToolTip(this.CheckBox_PreferListenPort, "Trys to bind socket to listen port, then returns queried result.");
            this.CheckBox_PreferListenPort.UseVisualStyleBackColor = true;
            // 
            // Text_NAT_Type
            // 
            this.Text_NAT_Type.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Text_NAT_Type.Location = new System.Drawing.Point(121, 107);
            this.Text_NAT_Type.Name = "Text_NAT_Type";
            this.Text_NAT_Type.ReadOnly = true;
            this.Text_NAT_Type.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Text_NAT_Type.Size = new System.Drawing.Size(135, 20);
            this.Text_NAT_Type.TabIndex = 32;
            this.Text_NAT_Type.Text = "unknown";
            // 
            // Label_STUN_Server_secondary
            // 
            this.Label_STUN_Server_secondary.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Label_STUN_Server_secondary.Location = new System.Drawing.Point(16, 56);
            this.Label_STUN_Server_secondary.Name = "Label_STUN_Server_secondary";
            this.Label_STUN_Server_secondary.Size = new System.Drawing.Size(99, 20);
            this.Label_STUN_Server_secondary.TabIndex = 25;
            this.Label_STUN_Server_secondary.Text = "STUN Server (alt)";
            this.Label_STUN_Server_secondary.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Input_HolePunch_Address
            // 
            this.Input_HolePunch_Address.Location = new System.Drawing.Point(111, 35);
            this.Input_HolePunch_Address.Name = "Input_HolePunch_Address";
            this.Input_HolePunch_Address.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Input_HolePunch_Address.Size = new System.Drawing.Size(95, 20);
            this.Input_HolePunch_Address.TabIndex = 18;
            this.Input_HolePunch_Address.Text = "0.0.0.0";
            this.Input_HolePunch_Address.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // Label_NAT_Type
            // 
            this.Label_NAT_Type.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Label_NAT_Type.Location = new System.Drawing.Point(6, 106);
            this.Label_NAT_Type.Name = "Label_NAT_Type";
            this.Label_NAT_Type.Size = new System.Drawing.Size(109, 20);
            this.Label_NAT_Type.TabIndex = 31;
            this.Label_NAT_Type.Text = "NAT Type";
            this.Label_NAT_Type.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Label_Outbound_Behavior
            // 
            this.Label_Outbound_Behavior.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Label_Outbound_Behavior.Location = new System.Drawing.Point(6, 131);
            this.Label_Outbound_Behavior.Name = "Label_Outbound_Behavior";
            this.Label_Outbound_Behavior.Size = new System.Drawing.Size(109, 20);
            this.Label_Outbound_Behavior.TabIndex = 34;
            this.Label_Outbound_Behavior.Text = "Outbound Behavior";
            this.Label_Outbound_Behavior.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Text_Outbound_Behavior
            // 
            this.Text_Outbound_Behavior.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.Text_Outbound_Behavior.Location = new System.Drawing.Point(121, 133);
            this.Text_Outbound_Behavior.Name = "Text_Outbound_Behavior";
            this.Text_Outbound_Behavior.ReadOnly = true;
            this.Text_Outbound_Behavior.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Text_Outbound_Behavior.Size = new System.Drawing.Size(135, 20);
            this.Text_Outbound_Behavior.TabIndex = 35;
            this.Text_Outbound_Behavior.Text = "unknown";
            // 
            // Label_IncomingGrp
            // 
            this.Label_IncomingGrp.Location = new System.Drawing.Point(3, 20);
            this.Label_IncomingGrp.Name = "Label_IncomingGrp";
            this.Label_IncomingGrp.Size = new System.Drawing.Size(109, 20);
            this.Label_IncomingGrp.TabIndex = 36;
            this.Label_IncomingGrp.Text = "Incoming Group";
            this.Label_IncomingGrp.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Text_IncomingGrp
            // 
            this.Text_IncomingGrp.Location = new System.Drawing.Point(118, 21);
            this.Text_IncomingGrp.Name = "Text_IncomingGrp";
            this.Text_IncomingGrp.ReadOnly = true;
            this.Text_IncomingGrp.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Text_IncomingGrp.Size = new System.Drawing.Size(171, 20);
            this.Text_IncomingGrp.TabIndex = 37;
            this.Text_IncomingGrp.Text = "unknown";
            // 
            // Label_OutgoingGrp
            // 
            this.Label_OutgoingGrp.Location = new System.Drawing.Point(3, 45);
            this.Label_OutgoingGrp.Name = "Label_OutgoingGrp";
            this.Label_OutgoingGrp.Size = new System.Drawing.Size(109, 20);
            this.Label_OutgoingGrp.TabIndex = 38;
            this.Label_OutgoingGrp.Text = "Outgoing Group";
            this.Label_OutgoingGrp.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Text_OutgoingGrp
            // 
            this.Text_OutgoingGrp.Location = new System.Drawing.Point(118, 47);
            this.Text_OutgoingGrp.Name = "Text_OutgoingGrp";
            this.Text_OutgoingGrp.ReadOnly = true;
            this.Text_OutgoingGrp.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Text_OutgoingGrp.Size = new System.Drawing.Size(171, 20);
            this.Text_OutgoingGrp.TabIndex = 39;
            this.Text_OutgoingGrp.Text = "unknown";
            // 
            // Label_OutgoingGrpRemote
            // 
            this.Label_OutgoingGrpRemote.Location = new System.Drawing.Point(3, 119);
            this.Label_OutgoingGrpRemote.Name = "Label_OutgoingGrpRemote";
            this.Label_OutgoingGrpRemote.Size = new System.Drawing.Size(109, 20);
            this.Label_OutgoingGrpRemote.TabIndex = 43;
            this.Label_OutgoingGrpRemote.Text = "Outgoing Group";
            this.Label_OutgoingGrpRemote.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Label_IncomingGrpRemote
            // 
            this.Label_IncomingGrpRemote.Location = new System.Drawing.Point(3, 94);
            this.Label_IncomingGrpRemote.Name = "Label_IncomingGrpRemote";
            this.Label_IncomingGrpRemote.Size = new System.Drawing.Size(109, 20);
            this.Label_IncomingGrpRemote.TabIndex = 41;
            this.Label_IncomingGrpRemote.Text = "Incoming Group";
            this.Label_IncomingGrpRemote.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Combo_IncomingGrp
            // 
            this.Combo_IncomingGrp.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Combo_IncomingGrp.FormattingEnabled = true;
            this.Combo_IncomingGrp.Location = new System.Drawing.Point(118, 95);
            this.Combo_IncomingGrp.Name = "Combo_IncomingGrp";
            this.Combo_IncomingGrp.Size = new System.Drawing.Size(171, 21);
            this.Combo_IncomingGrp.TabIndex = 47;
            this.Combo_IncomingGrp.SelectedIndexChanged += new System.EventHandler(this.Combo_IncomingGrp_SelectedIndexChanged);
            // 
            // Combo_OutgoingGrp
            // 
            this.Combo_OutgoingGrp.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Combo_OutgoingGrp.FormattingEnabled = true;
            this.Combo_OutgoingGrp.Location = new System.Drawing.Point(118, 122);
            this.Combo_OutgoingGrp.Name = "Combo_OutgoingGrp";
            this.Combo_OutgoingGrp.Size = new System.Drawing.Size(171, 21);
            this.Combo_OutgoingGrp.TabIndex = 48;
            this.Combo_OutgoingGrp.SelectedIndexChanged += new System.EventHandler(this.Combo_OutgoingGrp_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(295, 72);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(109, 20);
            this.label1.TabIndex = 50;
            this.label1.Text = "Technique Required";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Text_Technique_Required
            // 
            this.Text_Technique_Required.Location = new System.Drawing.Point(410, 72);
            this.Text_Technique_Required.Name = "Text_Technique_Required";
            this.Text_Technique_Required.ReadOnly = true;
            this.Text_Technique_Required.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Text_Technique_Required.Size = new System.Drawing.Size(135, 20);
            this.Text_Technique_Required.TabIndex = 51;
            this.Text_Technique_Required.Text = "unknown";
            // 
            // label2
            // 
            this.label2.Location = new System.Drawing.Point(295, 97);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(109, 20);
            this.label2.TabIndex = 52;
            this.label2.Text = "Extra Condition";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Text_ExtraCondition
            // 
            this.Text_ExtraCondition.Location = new System.Drawing.Point(410, 98);
            this.Text_ExtraCondition.Name = "Text_ExtraCondition";
            this.Text_ExtraCondition.ReadOnly = true;
            this.Text_ExtraCondition.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Text_ExtraCondition.Size = new System.Drawing.Size(135, 20);
            this.Text_ExtraCondition.TabIndex = 53;
            this.Text_ExtraCondition.Text = "unknown";
            // 
            // label3
            // 
            this.label3.Location = new System.Drawing.Point(295, 123);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(109, 20);
            this.label3.TabIndex = 54;
            this.label3.Text = "Recommended Role";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // Text_RecommendedRole
            // 
            this.Text_RecommendedRole.Location = new System.Drawing.Point(410, 124);
            this.Text_RecommendedRole.Name = "Text_RecommendedRole";
            this.Text_RecommendedRole.ReadOnly = true;
            this.Text_RecommendedRole.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.Text_RecommendedRole.Size = new System.Drawing.Size(135, 20);
            this.Text_RecommendedRole.TabIndex = 55;
            this.Text_RecommendedRole.Text = "unknown";
            // 
            // panel2
            // 
            this.panel2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel2.Controls.Add(this.CheckBox_PreferListenPort);
            this.panel2.Controls.Add(this.Button_QueryPort);
            this.panel2.Controls.Add(this.Button_HolePunchQueried);
            this.panel2.Controls.Add(this.CheckBox_RequirePassword);
            this.panel2.Controls.Add(this.Text_QueriedPort);
            this.panel2.Controls.Add(this.Button_HolePunch);
            this.panel2.Controls.Add(this.Input_PortToFree);
            this.panel2.Controls.Add(this.Label_PortToFree);
            this.panel2.Controls.Add(this.Label_PeerAddress);
            this.panel2.Controls.Add(this.Input_HolePunch_Address);
            this.panel2.Controls.Add(this.Input_HolePunch_Port);
            this.panel2.Controls.Add(this.Input_Password);
            this.panel2.Controls.Add(this.Button_PortForward);
            this.panel2.Location = new System.Drawing.Point(353, 32);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(295, 169);
            this.panel2.TabIndex = 56;
            // 
            // CheckBox_RequirePassword
            // 
            this.CheckBox_RequirePassword.AutoSize = true;
            this.CheckBox_RequirePassword.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.CheckBox_RequirePassword.Location = new System.Drawing.Point(12, 63);
            this.CheckBox_RequirePassword.Name = "CheckBox_RequirePassword";
            this.CheckBox_RequirePassword.Size = new System.Drawing.Size(112, 17);
            this.CheckBox_RequirePassword.TabIndex = 38;
            this.CheckBox_RequirePassword.Text = "Require Password";
            this.CheckBox_RequirePassword.UseVisualStyleBackColor = true;
            this.CheckBox_RequirePassword.CheckedChanged += new System.EventHandler(this.CheckBox_RequirePassword_CheckedChanged);
            // 
            // panel3
            // 
            this.panel3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel3.Controls.Add(this.Button_CheckNATType);
            this.panel3.Controls.Add(this.Input_StunServer);
            this.panel3.Controls.Add(this.Label_STUN_Server);
            this.panel3.Controls.Add(this.Input_StunServer_Port);
            this.panel3.Controls.Add(this.Label_ConnectionAttempts);
            this.panel3.Controls.Add(this.Input_ConnectionAttempts);
            this.panel3.Controls.Add(this.Label_TimeOut);
            this.panel3.Controls.Add(this.Input_TimeOut);
            this.panel3.Controls.Add(this.Label_PublicIP);
            this.panel3.Controls.Add(this.TextBox_PublicIP);
            this.panel3.Controls.Add(this.Label_STUN_Server_secondary);
            this.panel3.Controls.Add(this.Input_StunServer_2);
            this.panel3.Controls.Add(this.Input_StunServer2_Port);
            this.panel3.Controls.Add(this.Label_NAT_Type);
            this.panel3.Controls.Add(this.Text_NAT_Type);
            this.panel3.Controls.Add(this.Label_Outbound_Behavior);
            this.panel3.Controls.Add(this.Text_Outbound_Behavior);
            this.panel3.Location = new System.Drawing.Point(12, 32);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(335, 169);
            this.panel3.TabIndex = 57;
            // 
            // panel1
            // 
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(Local);
            this.panel1.Controls.Add(this.Label_IncomingGrp);
            this.panel1.Controls.Add(this.Text_IncomingGrp);
            this.panel1.Controls.Add(this.Label_OutgoingGrp);
            this.panel1.Controls.Add(this.Text_OutgoingGrp);
            this.panel1.Controls.Add(this.Text_RecommendedRole);
            this.panel1.Controls.Add(this.Combo_IncomingGrp);
            this.panel1.Controls.Add(this.label3);
            this.panel1.Controls.Add(Label_Remote);
            this.panel1.Controls.Add(this.Text_ExtraCondition);
            this.panel1.Controls.Add(this.Label_IncomingGrpRemote);
            this.panel1.Controls.Add(this.label2);
            this.panel1.Controls.Add(this.Label_OutgoingGrpRemote);
            this.panel1.Controls.Add(this.Text_Technique_Required);
            this.panel1.Controls.Add(this.Combo_OutgoingGrp);
            this.panel1.Controls.Add(this.label1);
            this.panel1.Location = new System.Drawing.Point(12, 227);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(636, 196);
            this.panel1.TabIndex = 58;
            // 
            // winFormMain
            // 
            this.ClientSize = new System.Drawing.Size(654, 716);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(Label_Result);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.panel3);
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.TextBox_Output);
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
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            this.panel3.ResumeLayout(false);
            this.panel3.PerformLayout();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.Label Label_NAT_Type;
        private System.Windows.Forms.TextBox Text_NAT_Type;

        private System.Windows.Forms.TextBox Input_Password;

        private System.Windows.Forms.NumericUpDown Input_StunServer2_Port;

        private System.Windows.Forms.Label Label_STUN_Server_secondary;
        private System.Windows.Forms.TextBox Input_StunServer_2;

        private void Combo_IncomingGrp_SelectedIndexChanged(object sender, EventArgs e) {
            EvaluateConnectionCompatibility();
        }

        private void Combo_OutgoingGrp_SelectedIndexChanged(object sender, EventArgs e) {
            EvaluateConnectionCompatibility();
        }

        private void CheckBox_RequirePassword_CheckedChanged(object sender, EventArgs e) {
            bool isChecked = CheckBox_RequirePassword.Checked;
            Input_Password.Enabled = isChecked;
            stunMageClient.PasswordHasToMatch = isChecked;
        }

        private async void Button_QueryPort_Click(object sender, EventArgs e) {
            if (queriedSocket == null) {
                if (!stunMageClient.TryResolveHostName(Input_StunServer.Text, out var iPAddress_1)) {
                    return;
                }
                try {
                    ushort portToBind = CheckBox_PreferListenPort.Checked ? (ushort)Input_PortToFree.Value : (ushort)0;
                    var res = await stunMageClient.GetSocketWithQueriedExternalEndPoint(new IPEndPoint(iPAddress_1, (int)Input_StunServer_Port.Value), portToBind);
                    queriedSocket = res.socket;
                    if(res.externalEndPoint.Address == IPAddress.Any) {
                        Log($"Queried IP Address is invalid, something went wrong: {res.externalEndPoint}");
                    }
                    else {
                        Log($"Bound socket to external endpoint: {res.externalEndPoint}");
                    }
                    TextBox_PublicIP.Text = res.externalEndPoint.Address.ToString();
                    Text_QueriedPort.Text = res.externalEndPoint.Port.ToString();
                    Button_QueryPort.Text = "Dispose";
                    Button_HolePunchQueried.Enabled = true;
                    Button_HolePunch.Enabled = false;
                    Input_PortToFree.Enabled = false;
                }
                catch (Exception ex){
                    Log($"{ex.GetType().Name}: {ex.Message}");
                }
                
            }
            else {
                DisposeQueriedSocket();
            }
        }

        private void DisposeQueriedSocket() {
            if (queriedSocket != null) {
                queriedSocket.Dispose();
                queriedSocket = null;
            }
            Text_QueriedPort.Text = "unknown";
            Button_QueryPort.Text = "Bind Queried Port";
            Button_HolePunchQueried.Enabled = false;
            Button_HolePunch.Enabled = true;
            Input_PortToFree.Enabled = true;
        }

        private void SaveServerSettings(string key, string value) {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            if(config == null) {
                ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
                config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
                config.Save(ConfigurationSaveMode.Modified, true);
            }
            if(config.AppSettings.Settings[key] == null) {
                config.AppSettings.Settings.Add(key, value);
            }
            else {
                config.AppSettings.Settings[key].Value = value;
            }
            config.AppSettings.Settings[key].Value = value;
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }
}