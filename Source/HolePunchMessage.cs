using System;
using System.Net;
using System.Text;
namespace STUN {
	public struct HolePunchMessage {
		public enum MessageType : byte {
			None = 0,
			Request = 1,
			Response = 2,
		}

		public IPEndPoint mirroredEndPoint;
		public MessageType type;
		public string Payload;

		public void Parse(byte[] data) {
			int offset = 0;

			type = ParseMessageType(data, ref offset);

			if (type == MessageType.Response) {
				mirroredEndPoint = ParseEndPoint(data, ref offset);
			}

			Payload = ParsePayLoad(data, ref offset);

		}

		private string ParsePayLoad(byte[] data, ref int offset) {
			byte[] header = new byte[4];
			Buffer.BlockCopy(data, offset, header, 0, 4);
			offset += 4;
			int payLoadLength = (header[0] << 24 | header[1] << 16 | header[2] << 8 | header[3]);
			payLoadLength = Math.Min(payLoadLength, 400);
			if (payLoadLength == 0) {
				return string.Empty;
			}
			
			byte[] payloadData = new byte[payLoadLength];
			Buffer.BlockCopy(data, offset, payloadData, 0, payLoadLength);
			offset += payLoadLength;
			return Encoding.UTF8.GetString(payloadData);
			// var str = Encoding.UTF8.GetString(data, offset)
		}

		private IPEndPoint ParseEndPoint(byte[] data, ref int offset) {
			int port = (data[offset++] << 8 | data[offset++]);
			byte[] ip = new byte[4];
			ip[0] = data[offset++];
			ip[1] = data[offset++];
			ip[2] = data[offset++];
			ip[3] = data[offset++];

			return new IPEndPoint(new IPAddress(ip), port);
		}

		private MessageType ParseMessageType(byte[] data, ref int offset) {
			return (MessageType)data[offset++];
		}

		public byte[] ToByteArray() {
			int offset = 0;

			int dataSize = 1 + 2 + 4;

			byte[] payLoadData = null;
			byte[] payLoadHeaderData = null;
			bool hasPayLoad = !string.IsNullOrEmpty(Payload);
			if (hasPayLoad) {
				payLoadData = Encoding.UTF8.GetBytes(Payload);	
				var payLoadLength = payLoadData.Length;
				payLoadLength = Math.Min(payLoadLength, 400);
				payLoadHeaderData = new byte[sizeof(int)];
				payLoadHeaderData[0] = (byte)(payLoadLength >> 24);
				payLoadHeaderData[1] = (byte)(payLoadLength >> 16);
				payLoadHeaderData[2] = (byte)(payLoadLength >> 8);
				payLoadHeaderData[3] = (byte)payLoadLength;

				dataSize += payLoadData.Length + payLoadHeaderData.Length;
			}
			

			byte[] data = new byte[dataSize];

			// Type
			data[offset++] = (byte)type;

			if (type == MessageType.Response) {
				// Port
				data[offset++] = (byte)(mirroredEndPoint.Port >> 8);
				data[offset++] = (byte)(mirroredEndPoint.Port & 0xFF);
				// Address
				byte[] ipBytes = mirroredEndPoint.Address.GetAddressBytes();
				Buffer.BlockCopy(ipBytes, 0, data, offset, ipBytes.Length);
				offset += ipBytes.Length;
			}

			// Payload
			// Header
			if (hasPayLoad) {
				Buffer.BlockCopy(payLoadHeaderData, 0, data, offset, payLoadHeaderData.Length);
				offset += payLoadHeaderData.Length;
				// Data
				Buffer.BlockCopy(payLoadData, 0, data, offset, payLoadData.Length);
				offset += payLoadData.Length;
			}

			return data;
		}
	}
}