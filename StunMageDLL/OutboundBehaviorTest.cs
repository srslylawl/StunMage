using System.Net;

namespace STUN {
	public enum OutboundBehaviorType {
		/// <summary>
		///External Port matches Local Port and doesn't change for different remote IPs
		/// </summary>
		Predictable_And_Consistent = 1,

		/// <summary>
		///External Port matches Local Port for the first remote IP Address contacted
		/// </summary>
		Predictable_Once_Per_IP = 2,

		/// <summary>
		///External Port matches Local Port for the first remote IP + Port combination only
		/// </summary>
		Predictable_Once = 3,

		/// <summary>
		///External Port does not match Local Port but remains the same for different remote IPs - can be queried.
		/// </summary>
		UnpredictableButConsistent = 4,

		/// <summary>
		///External Port does not match Local Port but remains the same for all ports per IP
		/// </summary>
		UnpredictableButConsistent_Per_IP = 5,

		/// <summary>
		///External Port does not match Local Port and changes for every IP + Port combination
		/// </summary>
		Unpredictable = 6
    }
	public class OutboundBehaviorTest {
		public IPEndPoint LocalEndPoint;

		public IPEndPoint External_1_1;
		public IPEndPoint External_1_2;
		public IPEndPoint External_2_1;
		public IPEndPoint External_2_2;

		public void SetEndPoint(int server, int port, IPEndPoint endPoint) {
			if (server == 0) {
				if (port == 0) {
					External_1_1 = endPoint;
					return;
				}

				External_1_2 = endPoint;
				return;
			}
			if (port == 0) {
				External_2_1 = endPoint;
				return;
			}
			External_2_2 = endPoint;
		}

		public override string ToString() {
			var str = $"====Behavior Test Results for {LocalEndPoint}=====\r\n";
				str += $"| Test 1: {External_1_1.Port} (base IP / base Port) |\r\n";
				str += $"| Test 2: {External_1_2.Port} (base IP / alt Port)  |\r\n";
				str += $"| Test 3: {External_2_1.Port} (alt IP / base Port)  |\r\n";
				str += $"| Test 4: {External_2_2.Port} (alt IP / alt Port)   |\r\n";
				str += $"____________________";
			return str;
		}

		//best-case scenario - whatever port we bind to will be used externally and remains the same for different ips
		public bool PortsAreIdentical() {
			return (LocalEndPoint.Port == External_1_1.Port) &&
				   (External_1_1.Port == External_1_2.Port) &&
				   (External_1_2.Port == External_2_1.Port) &&
				   (External_2_1.Port == External_2_2.Port);
		}

		//ok scenario - external is predictable initially, does however changes per different ip
		public bool PortsArePredictableOncePerIP() {
			return (LocalEndPoint.Port == External_1_1.Port) &&
						  (External_1_1.Port == External_1_2.Port) &&
						  (External_2_1.Port == External_2_2.Port) &&
						  (External_2_1.Port != External_1_1.Port);
		}

		//ok - external is predictable once per ip and port
		public bool PortsArePredictableOncePerPort() {
			return (LocalEndPoint.Port == External_1_1.Port) &&
						  (External_1_1.Port != External_1_2.Port);
		}

		//inconvenient - external endpoint does not map to local endpoint, however stays the same when pinging a different ip
		public bool PortsAreUnpredictableButConsistent() {
			return (LocalEndPoint.Port != External_1_1.Port) &&
				(External_1_1.Port == External_1_2.Port) &&
				(External_1_2.Port == External_2_1.Port) &&
				(External_2_1.Port == External_2_2.Port);
        }

		public bool PortsAreUnpredictableButConsistentPerIP() {
			return (LocalEndPoint.Port != External_1_1.Port) &&
				(External_1_1.Port == External_1_2.Port) &&
				(External_1_2.Port != External_2_1.Port) &&
				(External_2_1.Port == External_2_2.Port);
		}

		public static OutboundBehaviorType OutBoundBehaviorIsPredictable(params OutboundBehaviorTest[] tests) {
			OutboundBehaviorType worstCase = OutboundBehaviorType.Predictable_And_Consistent;

			foreach (var outboundBehaviorTest in tests) {
				OutboundBehaviorType thisCase;
				if (outboundBehaviorTest.PortsAreIdentical()) {
					continue;
                }

				if(outboundBehaviorTest.PortsArePredictableOncePerIP()) {
					thisCase = OutboundBehaviorType.Predictable_Once_Per_IP;
                }
				else if(outboundBehaviorTest.PortsArePredictableOncePerPort()) {
					thisCase = OutboundBehaviorType.Predictable_Once;
				}
				else if (outboundBehaviorTest.PortsAreUnpredictableButConsistent()) {
					thisCase = OutboundBehaviorType.UnpredictableButConsistent;
                }
				else if (outboundBehaviorTest.PortsAreUnpredictableButConsistentPerIP()) {
					thisCase = OutboundBehaviorType.UnpredictableButConsistent_Per_IP;
                }
				else {
					thisCase = OutboundBehaviorType.Unpredictable;
                }
				
				if((int)thisCase > (int)worstCase) {
					worstCase = thisCase;
                }
			}

			return worstCase;
		}
		
	}
}