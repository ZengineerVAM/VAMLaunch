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

namespace ScriptPlayer.Shared
{
    public static class CommandConverter
    {
        public static uint LaunchToVorzeSpeed(DeviceCommandInformation info)
        {
            //Information from https://github.com/metafetish/syncydink/blob/4c8c31d6f8ffba2c9d1f3fcb69209630b209cd89/src/utils/HapticsToButtplug.ts#L186

            double delta = Math.Abs(info.PositionFromOriginal - (double)info.PositionToOriginal) / 99.0;
            double speed = Math.Floor(25000 * Math.Pow(info.Duration.TotalMilliseconds / delta, -1.05)) / 100.0;
            // 100ms = ~0.95
            speed = info.TransformSpeed(speed) * 100.0;

            return (uint)speed;
        }

        // Reverted to 0.0 by request of github user "sextoydb":
        // https://github.com/FredTungsten/ScriptPlayer/issues/64
        public static double LaunchPositionToVibratorSpeed(byte position)
        {
            const double max = 1.0;
            const double min = 0.0;

            double speedRelative = 1.0 - ((position + 1) / 100.0);
            double result = min + (max - min) * speedRelative;
            return Math.Min(max, Math.Max(min, result));
        }

        public static double LaunchSpeedToVibratorSpeed(byte speed)
        {
            const double max = 1.0;
            const double min = 0.0;

            double speedRelative = (speed + 1) / 100.0;
            double result = min + (max - min) * speedRelative;
            return Math.Min(max, Math.Max(min, result));
        }

        public static uint LaunchToKiiroo(byte position, uint min, uint max)
        {
            double pos = position / 0.99;

            uint result = Math.Min(max, Math.Max(min, (uint)Math.Round(pos * (max - min) + min)));

            return result;
        }
    }
}
