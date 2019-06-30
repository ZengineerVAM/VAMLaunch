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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace ScriptPlayer.Shared
{
    public class Launch : Device
    {
        public bool SendCommandsWithResponse { get; set; } = false;

        // Just to make sure it doesn't get disposed or something like that
        // ReSharper disable once NotAccessedField.Local
        private BluetoothLEDevice _device;

        private readonly GattCharacteristic _commandCharacteristics;
        private readonly GattCharacteristic _notifyCharacteristics;
        private readonly GattCharacteristic _writeCharacteristics;

        private bool _initialized;
        

        public Launch(BluetoothLEDevice device, GattCharacteristic writeCharacteristics, GattCharacteristic notifyCharacteristics, GattCharacteristic commandCharacteristics)
        {
            _device = device;
            _writeCharacteristics = writeCharacteristics;
            _notifyCharacteristics = notifyCharacteristics;
            _commandCharacteristics = commandCharacteristics;

            Name = "Fleshlight Launch";
        }

        public async Task<bool> SetPosition(byte position, byte speed)
        {
            try
            {
                if (!await Initialize())
                    return false;

                GattWriteOption option = SendCommandsWithResponse
                    ? GattWriteOption.WriteWithResponse
                    : GattWriteOption.WriteWithoutResponse;

                IBuffer buffer = GetBuffer(position, speed);
                GattCommunicationStatus result = await _writeCharacteristics.WriteValueAsync(buffer, option);

                return result == GattCommunicationStatus.Success;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                OnDisconnected(e);
                return false;
            }
        }

        public async Task<bool> Initialize()
        {
            if (_initialized) return true;

            IBuffer buffer = GetBuffer(0);

            GattCommunicationStatus status = await _commandCharacteristics.WriteValueAsync(buffer);

            if (status != GattCommunicationStatus.Success)
                return false;

            _notifyCharacteristics.ValueChanged += NotifyCharacteristicsOnValueChanged;

            _initialized = true;
            return true;
        }

        private void NotifyCharacteristicsOnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] bytes = new byte[reader.UnconsumedBufferLength];

            reader.ReadBytes(bytes);

            Debug.WriteLine(string.Join("-", bytes.Select(b => b.ToString("X2"))));
        }

        private IBuffer GetBuffer(params byte[] data)
        {
            DataWriter writer = new DataWriter();
            writer.WriteBytes(data);
            return writer.DetachBuffer();
        }

        public static class Uids
        {
            public static Guid MainService = Guid.Parse("88f80580-0000-01e6-aace-0002a5d5c51b");
            public static Guid WriteCharacteristics = Guid.Parse("88f80581-0000-01e6-aace-0002a5d5c51b");
            public static Guid StatusNotificationCharacteristics = Guid.Parse("88f80582-0000-01e6-aace-0002a5d5c51b");
            public static Guid CommandCharacteristics = Guid.Parse("88f80583-0000-01e6-aace-0002a5d5c51b");
        }

        public override async Task Set(DeviceCommandInformation information)
        {
            await SetPosition(information.PositionToTransformed, information.SpeedTransformed);
        }

        public override async Task Set(IntermediateCommandInformation information)
        {
            return;
            // Does not apply
        }

        public override void Stop()
        {
            // Not available
        }

        public override  void Dispose()
        {
            base.Dispose();
            _device?.Dispose();
            _device = null;
        }
    }
}