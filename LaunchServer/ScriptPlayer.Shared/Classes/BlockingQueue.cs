/*
BSD 3-Clause License

Copyright (c) 2017, Fred Tungsten
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of the copyright holder nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace ScriptPlayer.Shared
{
    public class BlockingQueue<T> where T : class
    {
        private ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

        private readonly object _queueLock = new object();

        private bool _closed;

        public void Close()
        {
            try
            {
                if (_closed) return;

                lock (_queueLock)
                {
                    _closed = true;
                    Monitor.PulseAll(_queueLock);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }

        public int Count
        {
            get
            {
                lock (_queueLock)
                    return _queue.Count;
            }
        }

        public void Enqueue(T item)
        {
            if (_closed) return;

            lock (_queueLock)
            {
                _queue.Enqueue(item);
                Monitor.Pulse(_queueLock);
            }
        }

        public T Deqeue()
        {
            try
            {
                if (_closed) return null;

                lock (_queueLock)
                {
                    T result;

                    while (!_queue.TryDequeue(out result))
                    {
                        if (_closed)
                            return null;

                        Monitor.Wait(_queueLock);
                    }

                    return result;
                }
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }

        private void DeleteTill(Func<T, T, bool> func, T comparison)
        {
            lock (_queueLock)
            {
                ConcurrentQueue<T> newQueue = new ConcurrentQueue<T>();

                while (_queue.Count > 0)
                {
                    _queue.TryDequeue(out T item);

                    if (func(item, comparison))
                        break;

                    newQueue.Enqueue(item);
                }

                _queue = newQueue;
            }
        }

        public void ReplaceExisting(T item, Func<T,T, bool> condition)
        {
            lock (_queueLock)
            {
                DeleteTill(condition, item);
                Enqueue(item);
            }
        }

        public void Clear()
        {
            lock (_queueLock)
            {
                while (!_queue.IsEmpty)
                    _queue.TryDequeue(out T _);
            }
        }
    }
}