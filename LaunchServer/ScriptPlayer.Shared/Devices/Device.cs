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
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ScriptPlayer.Shared
{
    public abstract class Device : INotifyPropertyChanged, IDisposable
    {
        public event EventHandler<Exception> Disconnected;

        protected virtual void OnDisconnected(Exception e)
        {
            Dispose();
            Disconnected?.Invoke(this, e);
        }

        private bool _running;
        private bool _isEnabled;

        private readonly Thread _commandThread;
        private readonly BlockingQueue<QueueEntry<DeviceCommandInformation>> _queue = new BlockingQueue<QueueEntry<DeviceCommandInformation>>();

        public TimeSpan MinDelayBetweenCommands = TimeSpan.FromMilliseconds(166);
        public TimeSpan AcceptableCommandExecutionDelay = TimeSpan.FromMilliseconds(5);

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (value == _isEnabled) return;
                _isEnabled = value;

                if (!_isEnabled)
                    Stop();

                OnPropertyChanged();
            }
        }

        public string Name { get; set; }

        protected Device()
        {
            _running = true;
            _commandThread = new Thread(CommandLoop);
            _commandThread.Start();
        }

        private async void CommandLoop()
        {
            while (_running)
            {
                var entry = _queue.Deqeue();

                if (!_isEnabled)
                    continue;

                if (entry == null)
                    return;

                DateTime now = DateTime.Now;
                TimeSpan delay = now - entry.Submitted;

                if (delay > AcceptableCommandExecutionDelay)
                    Debug.WriteLine("Command Execution Delay: " + delay.ToString("g"));

                DeviceCommandInformation information = entry.Values;
                await Set(information);

                TimeSpan wait = DateTime.Now - now;
                if (wait < MinDelayBetweenCommands)
                    await Task.Delay(MinDelayBetweenCommands - wait);
            }
        }

        public void Close()
        {
            _running = false;
            _queue.Close();

            if (!_commandThread.Join(TimeSpan.FromMilliseconds(500)))
                _commandThread.Abort();
        }

        public void Enqueue(DeviceCommandInformation information)
        {
            _queue.ReplaceExisting(new QueueEntry<DeviceCommandInformation>(information), CompareCommandInformation);
        }

        private bool CompareCommandInformation(QueueEntry<DeviceCommandInformation> arg1, QueueEntry<DeviceCommandInformation> arg2)
        {
            return CommandsAreSimilar(arg1.Values, arg2.Values);
        }

        protected virtual bool CommandsAreSimilar(DeviceCommandInformation command1, DeviceCommandInformation command2)
        {
            return Math.Abs(command1.PositionToTransformed - command2.PositionToTransformed) < 10;
        }

        public abstract Task Set(DeviceCommandInformation information);

        public abstract Task Set(IntermediateCommandInformation information);

        public abstract void Stop();
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public virtual void Dispose()
        {
            Close();
        }
    }
}
