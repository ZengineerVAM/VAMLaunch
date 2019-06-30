using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace VAMLaunchPlugin
{
    public class VAMLaunchNetwork
    {
        private UdpClient _udpClient;
        private Thread _recvThread;
        private bool _listening;

        private IPEndPoint _sendEndPoint;
        
        private readonly Queue<byte[]> _recvQueue = new Queue<byte[]>();
        public int QueuedMsgCount => _recvQueue.Count;

        ~VAMLaunchNetwork()
        {
            Stop();
        }
        
        public bool Init(string serverIp, int recvPort, int sendPort)
        {
            try
            {
                var address = IPAddress.Parse(serverIp);
                _sendEndPoint = new IPEndPoint(address, sendPort);
            }
            catch (Exception e)
            {
                SuperController.LogMessage(e.Message);
                return false;
            }

            try
            {
                _udpClient = new UdpClient(recvPort);
            }
            catch (Exception e)
            {
                SuperController.LogMessage(string.Format("Failed to init recv on port {0} ({1})", recvPort,
                    e.Message));
                return false;
            }
            
            _recvThread = new Thread(Receive);
            _recvThread.IsBackground = true;
            _listening = true;
            _recvThread.Start();

            return true;
        }

        private void Receive()
        {
            IPEndPoint recvEndPoint = new IPEndPoint(IPAddress.Any, 0);
            
            while (_listening)
            {
                try
                {
                    byte[] recvBytes = _udpClient.Receive(ref recvEndPoint);
                    lock (_recvQueue)
                    {
                        _recvQueue.Enqueue(recvBytes);
                    }
                }
                catch (SocketException e)
                {
                    // 10004 thrown when socket is closed
                    //if (e.ErrorCode != 10004)
                    //{
                        //SuperController.LogMessage("Socket exception while receiving data from udp client: " + e.Message);                        
                    //}
                }
                catch (Exception e)
                {
                    //SuperController.LogMessage("Error receiving data from udp client: " + e.Message);
                }
            }
        }

        public void Send(byte[] data, int length)
        {
            try
            {
                _udpClient.Send(data, length, _sendEndPoint);
            }
            catch (Exception e)
            {
                
            }
        }

        public void Stop()
        {
            _listening = false;
            if (_recvThread != null && _recvThread.IsAlive)
            {
                _recvThread.Abort();
            }

            if (_udpClient != null)
            {
                _udpClient.Close();
            }
        }

        public byte[] GetNextMessage()
        {
            return _recvQueue.Count > 0 ? _recvQueue.Dequeue() : null;
        }
    }
}