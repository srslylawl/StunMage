using System.Net;

namespace STUN {
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

		public bool PortsAreIdentical() {
			return (LocalEndPoint.Port == External_1_1.Port) &&
				   (External_1_1.Port == External_1_2.Port) &&
				   (External_1_2.Port == External_2_1.Port) &&
				   (External_2_1.Port == External_2_2.Port);
		}

		public bool PortsAreIdenticalInitially() {
			return (LocalEndPoint.Port == External_1_1.Port) &&
						  (External_1_1.Port == External_1_2.Port) &&
						  (External_2_1.Port == External_2_2.Port);
		}

		public bool PortsArePredictable(out int delta) {
			delta = 0;

			if (!PortsAreIdentical() || !PortsAreIdenticalInitially())
				return false;

			if (PortsAreIdentical()) {
				delta = 0;
				return true;
			}
			
			var delta1 = External_1_1.Port - External_1_2.Port;
			var delta2 = External_2_1.Port - External_2_2.Port;

			if (delta1 != delta2) {
				return false;
			}

			var delta3 = External_1_2.Port - External_2_1.Port;

			delta = delta3;

			return true;
		}

		public static bool OutBoundBehaviorIsPredictable(params OutboundBehaviorTest[] tests) {
			foreach (var outboundBehaviorTest in tests) {
				if (!outboundBehaviorTest.PortsAreIdentical()) return false;
			}

			return true;
		}
		
	}
}