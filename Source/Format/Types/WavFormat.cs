﻿using System;
using System.Collections.Generic;
using System.IO;
using NongIssue;
using NongCrypto;

namespace NongFormat
{
    enum WaveCompression
    { Unknown=0, PCM=1, MS_ADPCM=2, ITUG711alaw=6, ITUG711Âµlaw=7, IMA_ADPCM=17, GSM610=49, MPEG=80}

    // www.sonicspot.com/guide/wavefiles.html (broken?)
    // wiki.audacityteam.org/wiki/WAV
    public sealed class WavFormat : RiffContainer
    {
        public static string[] Names
        { get { return new string[] { "wav" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x28 && hdr[0x00]=='R' && hdr[0x01]=='I' && hdr[0x02]=='F' && hdr[0x03]=='F'
                                   && hdr[0x08]=='W' && hdr[0x09]=='A' && hdr[0x0A]=='V' && hdr[0x0B]=='E')
                return new Model (stream, hdr, path);
            return null;
        }


        public new class Model : RiffContainer.Model
        {
            public readonly WavFormat Bind;

            public Model (Stream stream, byte[] hdr, string path)
            {
                BaseBind = RiffBind = Bind = new WavFormat (stream, path);
                Bind.Issues = IssueModel.Bind;

                ParseRiff (hdr);

                Bind.ActualCRC32 = null;

                if (Bind.Issues.HasFatal)
                    return;

                if (hdr.Length < 0x2C)
                {
                    IssueModel.Add ("File truncated near header", Severity.Fatal);
                    return;
                }

                if (Bind.RiffChunkCount > 1)
                {
                    IssueModel.Add ("Contains multiple RIFF chunks", Severity.Fatal);
                    return;
                }

                int hPos = 0x0C;
                if (hdr[hPos] != 'f' || hdr[hPos+1] != 'm' || hdr[hPos+2] != 't' || hdr[hPos+3] != 0x20)
                {
                    IssueModel.Add ("Missing 'fmt' section", Severity.Fatal);
                    return;
                }

                Bind.CompCode = hdr[hPos+8] | hdr[hPos+9] << 8;
                Bind.ChannelCount = hdr[hPos+0x0A] | hdr[hPos+0x0B] << 8;
                Bind.SampleRate = ConvertTo.FromLit32ToUInt32 (hdr, hPos+0x0C);
                Bind.AverageBPS = ConvertTo.FromLit32ToUInt32 (hdr, hPos+0x10);
                Bind.BlockAlign = hdr[hPos+0x14] | hdr[hPos+0x15] << 8;
                Bind.BitsPerSample = hdr[hPos+0x16] | hdr[hPos+0x17] << 8;

                if ((hdr[hPos] & 0x80) != 0)
                {
                    IssueModel.Add ("Header size insanely huge", Severity.Fatal);
                    return;
                }

                long hdrDataSize = ConvertTo.FromLit32ToInt32 (hdr, hPos+4);
                long dataPos = hPos + 8 + hdrDataSize;

                stream.Position = dataPos;
                var dHdr = new byte[8];
                if (stream.Read (dHdr, 0, 8) != 8)
                {
                    IssueModel.Add ("Read failed", Severity.Fatal);
                    return;
                }

                if (dHdr[0] != 'd' || dHdr[1] != 'a' || dHdr[2] != 't' || dHdr[3] != 'a')
                {
                    IssueModel.Add ("Missing 'data' section", Severity.Fatal);
                    return;
                }

                Bind.mediaPosition = dataPos + 8;
                Bind.MediaCount = ConvertTo.FromLit32ToUInt32 (dHdr, 4);
                if (Bind.mediaPosition + Bind.MediaCount > Bind.RiffSize)
                {
                    IssueModel.Add ("Invalid data size", Severity.Fatal);
                    return;
                }

                Bind.HasTags = Bind.mediaPosition + Bind.MediaCount < Bind.RiffSize;
                GetDiagnostics();
            }


            protected void GetDiagnostics()
            {
                GetRiffDiagnostics();

                if (Bind.CompCode != (int) WaveCompression.PCM)
                    IssueModel.Add ("Data is not PCM", Severity.Trivia, IssueTags.Substandard);
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (IssueModel.Bind.HasFatal)
                    return;

                if ((hashFlags & Hashes.PcmMD5) != 0 && Bind.actualMediaMD5 == null)
                    try
                    {
                        var hasher = new Md5Hasher();
                        hasher.Append (Bind.fbs, Bind.mediaPosition, Bind.MediaCount);
                        Bind.actualMediaMD5 = hasher.GetHashAndReset();
                    }
                    catch (EndOfStreamException)
                    {
                        IssueModel.Add ("File truncated near audio.", Severity.Fatal);
                    }

                if ((hashFlags & Hashes.PcmCRC32) != 0 && Bind.ActualCRC32 == null)
                    try
                    {
                        var hasher = new Crc32rHasher();
                        hasher.Append (Bind.fbs, Bind.mediaPosition, Bind.MediaCount);
                        var hash = hasher.GetHashAndReset();
                        Bind.ActualCRC32 = BitConverter.ToUInt32 (hash, 0);
                    }
                    catch (EndOfStreamException)
                    {
                        IssueModel.Add ("File truncated near audio.", Severity.Fatal);
                    }

                base.CalcHashes (hashFlags, validationFlags);
            }
        }

        public UInt32? ActualCRC32 { get; private set; }
        public bool HasTags { get; private set; }

        private byte[] actualMediaMD5;
        public string ActualMediaMD5ToHex { get { return ConvertTo.ToHexString (actualMediaMD5); } }

        public int CompCode { get; private set; }
        public int ChannelCount { get; private set; }
        public uint SampleRate { get; private set; }
        public uint AverageBPS { get; private set; }
        public int BlockAlign { get; private set; }
        public int BitsPerSample { get; private set; }


        private WavFormat (Stream stream, string path) : base (stream, path)
        { }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            base.GetDetailsBody (report, scope);

            if (ActualCRC32 != null)
                report.Add (String.Format ("Actual CRC-32 = 0x{0:X8}", ActualCRC32));
            if (actualMediaMD5 != null)
                report.Add ("Actual PCM data MD5 = " + ActualMediaMD5ToHex);

            report.Add ("Compression = " + (WaveCompression) CompCode);
            report.Add ("Number of channels = " + ChannelCount);
            report.Add ("Sample rate = " + SampleRate + " Hz");

            if (scope <= Granularity.Detail)
            {
                report.Add ("Average bytes per second = " + AverageBPS);
                report.Add ("Block align = " + BlockAlign + " bytes per sample slice");
                report.Add ("Significant bits per sample = " + BitsPerSample);

                string lx = "Layout = | Audio |";
                if (HasTags)
                    lx += " Tags |";
                report.Add (lx);
            }
        }
    }
}