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
    public class DeviceCommandInformation
    {
        public byte PositionFromTransformed;
        public byte PositionToTransformed;
        public byte SpeedTransformed;

        public byte PositionFromOriginal;
        public byte PositionToOriginal;
        public byte SpeedOriginal;

        public TimeSpan Duration;
        public double SpeedMultiplier { get; set; } = 1;
        public double SpeedMin { get; set; } = 0;
        public double SpeedMax { get; set; } = 1;
        public double PlaybackRate { get; set; } = 1;
        public TimeSpan DurationStretched { get; set; }

        public double TransformSpeed(double speed)
        {
            return Math.Min(SpeedMax, Math.Max(SpeedMin, speed * SpeedMultiplier));
        }
    }

    public class IntermediateCommandInformation
    {
        public DeviceCommandInformation DeviceInformation;
        public double Progress;
    }
}
