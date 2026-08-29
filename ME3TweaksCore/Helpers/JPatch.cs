/*
    A C# Implementation of JoJoDiff's JPatch functionality.
    Supports 0.8.4 and below diff files.
    Copyright (C) 2019-2023 Mgamerz

    Ported from the original implementation by Joris Heirbaut:
    http://jojodiff.sourceforge.net/

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */

// This implementation was written by ME3Tweaks.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace ME3TweaksCore.Helpers
{
    /// <summary>

    /// </summary>
    [Localizable(false)]
    public class JPatch
    {
        /// <summary>
        /// Forward-only buffered reader over the patch stream. Batches <see cref="Stream.Read"/>
        /// calls instead of paying a virtual-dispatch cost per byte via <see cref="Stream.ReadByte"/>,
        /// and exposes a lookahead span so runs of ordinary (non-opcode) bytes can be bulk-copied
        /// instead of processed one byte at a time.
        /// </summary>
        private sealed class PatchReader
        {
            private readonly Stream mStream;
            private readonly byte[] mBuf;
            private int mPos;
            private int mLen;
            private long mConsumed;

            public PatchReader(Stream stream, int bufferSize = 64 * 1024)
            {
                mStream = stream;
                mBuf = new byte[bufferSize];
            }

            /// <summary>
            /// Logical count of bytes returned so far via <see cref="ReadByte"/>/<see cref="Advance"/> --
            /// NOT the same as the underlying stream's Position, which may be read ahead into the buffer.
            /// </summary>
            public long Position => mConsumed;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int ReadByte()
            {
                if (mPos >= mLen && !Refill()) return EOF;
                mConsumed++;
                return mBuf[mPos++];
            }

            /// <summary>
            /// Currently-buffered, not-yet-consumed bytes (refilling from the stream if the buffer
            /// is empty). Does not advance the read cursor -- call <see cref="Advance"/> for that.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ReadOnlySpan<byte> PeekBuffered()
            {
                if (mPos >= mLen && !Refill()) return ReadOnlySpan<byte>.Empty;
                return new ReadOnlySpan<byte>(mBuf, mPos, mLen - mPos);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Advance(int count)
            {
                mPos += count;
                mConsumed += count;
            }

            private bool Refill()
            {
                mLen = mStream.Read(mBuf, 0, mBuf.Length);
                mPos = 0;
                return mLen > 0;
            }
        }

        /// <summary>
        /// Buffered writer over the output stream. Batches <see cref="Stream.Write"/> calls instead
        /// of paying a virtual-dispatch cost per byte via <see cref="Stream.WriteByte"/>. Must be
        /// flushed (see <see cref="Flush"/>) once writing is complete.
        /// </summary>
        private sealed class OutputWriter
        {
            private readonly Stream mStream;
            private readonly byte[] mBuf;
            private int mPos;

            // Separate scratch buffer for CopyFrom (EQL runs read directly from the source
            // stream). Owned per-OutputWriter-instance (i.e. per ApplyJPatch call) rather than
            // shared/static, so concurrent ApplyJPatch calls on different threads never race
            // on it the way a shared static buffer would.
            private byte[] mCopyBuf;

            public OutputWriter(Stream stream, int bufferSize = 64 * 1024)
            {
                mStream = stream;
                mBuf = new byte[bufferSize];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteByte(byte b)
            {
                if (mPos == mBuf.Length) FlushBuffer();
                mBuf[mPos++] = b;
            }

            public void Write(ReadOnlySpan<byte> data)
            {
                // Writes at least as large as the whole buffer bypass it entirely (after
                // flushing anything already pending) to avoid a redundant extra copy.
                if (data.Length >= mBuf.Length)
                {
                    FlushBuffer();
                    mStream.Write(data);
                    return;
                }

                if (mPos + data.Length > mBuf.Length) FlushBuffer();
                data.CopyTo(mBuf.AsSpan(mPos));
                mPos += data.Length;
            }

            public void Flush() => FlushBuffer();

            /// <summary>
            /// Copies <paramref name="bytes"/> bytes directly from <paramref name="source"/>
            /// into this writer, for EQL runs. Reuses a per-instance scratch buffer across
            /// calls instead of allocating one per invocation (an EQL-heavy patch can contain
            /// many separate equal runs).
            /// </summary>
            public void CopyFrom(Stream source, int bytes)
            {
                mCopyBuf ??= new byte[65536];

                int read;
                while (bytes > 0 &&
                       (read = source.Read(mCopyBuf, 0, Math.Min(mCopyBuf.Length, bytes))) > 0)
                {
                    Write(new ReadOnlySpan<byte>(mCopyBuf, 0, read));
                    bytes -= read;
                }
            }

            private void FlushBuffer()
            {
                if (mPos > 0)
                {
                    mStream.Write(mBuf, 0, mPos);
                    mPos = 0;
                }
            }
        }

        /// <summary>
        /// Opcodes used by JoJoDiff
        /// </summary>
        private enum JojoOpcode
        {
            /// <summary>
            /// Opening opcode that precedes other opcodes (unless followed by another ESC, in which case it deduces to a single ESC)
            /// </summary>
            OPERATION_ESC = 0xA7,
            /// <summary>
            /// Overwrite bytes (by writing from patch and advancing source position with it)
            /// </summary>
            OPERATION_MOD = 0xA6,
            /// <summary>
            /// Insert new byte (by writing new bytes without advancing the source)
            /// </summary>
            OPERATION_INS = 0xA5,
            /// <summary>
            /// Delete bytes (by skipping it in the source)
            /// </summary>
            OPERATION_DEL = 0xA4,
            /// <summary>
            /// Copy bytes (by copying directly from source to output)
            /// </summary>
            OPERATION_EQL = 0xA3,
            /// <summary>
            /// Backtrace source (move the source position backwards)
            /// </summary>
            OPERATION_BKT = 0xA2
        }

        /// <summary>
        /// End of file (-1)
        /// </summary>
        private const int EOF = -1;

        /// <summary>
        /// Apply a JPatch to a stream and put the result in another stream.
        /// </summary>
        /// <param name="sourceData">The source stream to patch against</param>
        /// <param name="patchData">The patch file stream</param>
        /// <param name="outData">The resulting patch output</param>
        /// <param name="percentDoneCallback">Function that is invoked as the patching cursor progresses through the patch file. Can be null.</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool ApplyJPatch(Stream sourceData, Stream patchData, Stream outData, Action<long, long> percentDoneCallback = null)
        {
            //all positions should be at 0.
            sourceData.Position = 0;
            patchData.Position = 0;
            outData.Position = 0;

            long patchLen = patchData.Length;
            var reader = new PatchReader(patchData);
            var writer = new OutputWriter(outData);

            // A patch built from a highly-fragmented diff (e.g. a full framework/build-number
            // change, where the algorithm finds little to align on) can decompose into
            // millions of tiny opcode operations -- one per byte moved -- and the loop below
            // invokes percentDoneCallback once per operation. Calling into a UI-bound
            // callback (property setters that trigger data-binding updates) millions of times
            // dwarfs JPatch's own work, so throttle to roughly every 0.5% of the patch file
            // (min 64KB) instead of every single operation. This does not change what the
            // callback reports -- still (bytes of patchData consumed, total patch length) --
            // only how often it's called.
            long progressStep = Math.Max(patchLen / 200, 64 * 1024);
            long nextProgressAt = 0;

            try
            {
                int readByte = 0;
                int peekChar1;
                int peekChar2;

                //Read patch data.
                while (readByte != EOF) // I don't think this condition will occur unless patch is malformed
                {
                    if (percentDoneCallback != null && reader.Position >= nextProgressAt)
                    {
                        percentDoneCallback(reader.Position, patchLen);
                        nextProgressAt = reader.Position + progressStep;
                    }
                    // Read operator from input, unless this has already been done
                    if (readByte == 0)
                    {
                        peekChar1 = reader.ReadByte();
                        if (peekChar1 == EOF)
                            break; // We have hit the end of the file

                        if (peekChar1 == (int)JojoOpcode.OPERATION_ESC)
                        {
                            peekChar2 = reader.ReadByte();
                            switch (peekChar2)
                            {
                                case (int)JojoOpcode.OPERATION_EQL:
                                case (int)JojoOpcode.OPERATION_DEL:
                                case (int)JojoOpcode.OPERATION_BKT:
                                case (int)JojoOpcode.OPERATION_MOD:
                                case (int)JojoOpcode.OPERATION_INS:
                                    readByte = peekChar2;
                                    peekChar2 = EOF;
                                    peekChar1 = EOF;
                                    break;
                                case EOF:
                                    // FAILED, EOF
                                    Debug.WriteLine(@"Unexpected end of patch file!");
                                    return false;
                                default:
                                    // ESC xxx or ESC ESC at the start of a sequence
                                    // Resolve by double pending bytes: peekChar1 and peekChar2
                                    readByte = (int)JojoOpcode.OPERATION_MOD;
                                    break;
                            }
                        }
                        else
                        {
                            // If an ESC <opr> is missing, set default operator (gaining two bytes)
                            readByte = (int)JojoOpcode.OPERATION_MOD;
                            peekChar2 = EOF;
                        }
                    }
                    else
                    {
                        peekChar1 = EOF; // only needed when switching between MOD and INS
                        peekChar2 = EOF;
                    }

                    //Perform operations
                    switch (readByte)
                    {
                        case (int)JojoOpcode.OPERATION_MOD:
                            //Overwrite data
                            readByte = processModInsOpcode(sourceData, reader, writer, true, peekChar1, peekChar2);
                            break;
                        case (int)JojoOpcode.OPERATION_INS:
                            //Insert new data
                            readByte = processModInsOpcode(sourceData, reader, writer, false, peekChar1, peekChar2);
                            break;
                        case (int)JojoOpcode.OPERATION_EQL:
                            //Equal, move pointers forward
                            processEqlBktOpcode(sourceData, reader, writer, false);
                            readByte = 0;
                            break;
                        case (int)JojoOpcode.OPERATION_BKT:
                            //Backtrace, move backwards
                            processEqlBktOpcode(sourceData, reader, writer, true);
                            readByte = 0;
                            break;
                        case (int)JojoOpcode.OPERATION_DEL:
                            //Delete, move the source data position forward without writing anything out
                            processDel(sourceData, reader);
                            readByte = 0; // Makes next op read fully (ESC 0xA7)
                            break;
                        default:
                            Debug.WriteLine(@"Unsupported opcode: " + readByte.ToString(@"X2"));
                            break;
                    }
                }

                if (reader.Position != patchLen)
                {
                    Debug.WriteLine(@"Didn't read until end of patch file!");
                }

                // Throttling above can leave the last reported position short of patchLen
                // (the final operation(s) may not cross another progressStep boundary) --
                // send one last call so a bound progress bar actually reaches completion.
                percentDoneCallback?.Invoke(patchLen, patchLen);

                return true; //OK
            }
            finally
            {
                // Flush unconditionally (success, the "unexpected EOF" early-return, or any
                // future early-return/exception path) so a caller that inspects outData after
                // a false return -- as one real call site does -- still sees exactly the bytes
                // that were "written" up to that point, matching the original unbuffered
                // WriteByte()-per-call behavior byte for byte.
                writer.Flush();
            }
        }

        /// <summary>
        /// Deletes data from the source stream by advancing the source data position without writing it to the output stream.
        /// </summary>
        /// <param name="sourceData">The sourcedata stream</param>
        /// <param name="patchData">The patchdata stream</param>
        private static void processDel(Stream sourceData, PatchReader patchData)
        {
            //Delete data from source. Essentially, skip the data.
            var len = getJLength(patchData);
            sourceData.Position += len;
        }

        /// <summary>
        /// Reads a length value from the patch stream
        /// </summary>
        /// <param name="patchData">The patch stream/param>
        /// <returns>A length value, up to 2GiB</returns>
        private static int getJLength(PatchReader patchData)
        {
            /*From Jojo diff source documentation:
            * 
            *   <length> :   1 to 5 bytes for specifying a 32-bit unsigned number.
            *              1 <= x < 252        1 byte:   0-251
            *            252 <= x < 508        2 bytes:  252, x-252
            *            508 <= x < 0x10000    3 bytes:  253, xx
            *        0x10000 <= x < 0x100000000        5 bytes:  254, xxxx
            *                          9 bytes:  255, xxxxxxxx
            *
            * Length will never be zero as that makes no sense, you would never advance
            * positions by 0 or read 0 bytes
            */
            var byte1 = patchData.ReadByte();
            if (byte1 <= 251)
            {
                return byte1 + 1;
            }
            else if (byte1 == 252)
            {
                return byte1 + patchData.ReadByte() + 1;
            }
            else if (byte1 == 253)
            {
                return (patchData.ReadByte() << 8) + patchData.ReadByte();
            }
            else if (byte1 == 254)
            {
                return (patchData.ReadByte() << 24) + (patchData.ReadByte() << 16) + (patchData.ReadByte() << 8) + patchData.ReadByte();
            }
            else
            {
                // LARGE FILE SUPPORT (0xFF) IS NOT SUPPORTED
                // IN THIS IMPLEMENTATION OF JPATCH
                Debug.WriteLine(@"64-bit length numbers are not supported by this implementation of JPatch");
                return -1;
            }
        }

        /// <summary>
        /// Modifies or Inserts a series of bytes. Specify MOD using modifyMode = true, so the source data pointer moves as each new byte is written in.
        /// </summary>
        /// <param name="sourceData">The sourcedata stream</param>
        /// <param name="patchData">The patchdata stream</param>
        /// <param name="outData">The output data stream</param>
        /// <param name="modifyMode">If this is a MOD opcode operation. Indicate false to operate in INS mode</param>
        /// <param name="peekChar1">The first peeked byte ahead of the opcode</param>
        /// <param name="peekChar2">The second peeked byte ahead of the opcode</param>
        /// <returns></returns>
        private static int processModInsOpcode(Stream sourceData, PatchReader patchData, OutputWriter outData, bool modifyMode, int peekChar1, int peekChar2)
        {
            // First write pending bytes
            if (peekChar1 != EOF)
            {
                outData.WriteByte((byte)peekChar1);
                if (modifyMode)
                {
                    sourceData.Position++;
                }

                if (peekChar1 == (int)JojoOpcode.OPERATION_ESC && peekChar2 != (int)JojoOpcode.OPERATION_ESC)
                {
                    outData.WriteByte((byte)peekChar2);
                    if (modifyMode)
                    {
                        sourceData.Position++;
                    }
                }
            }

            // Runs of ordinary (non-ESC) bytes are bulk-copied straight from the patch
            // reader's buffer to the output writer instead of going through ReadByte()/
            // WriteByte() one byte at a time -- this loop is the hottest path in JPatch
            // for edit-dense patches, since every MOD/INS byte passes through it.
            while (true)
            {
                ReadOnlySpan<byte> avail = patchData.PeekBuffered();
                if (avail.IsEmpty)
                {
                    return EOF;
                }

                int escIdx = avail.IndexOf((byte)JojoOpcode.OPERATION_ESC);
                if (escIdx < 0)
                {
                    // No ESC anywhere in the currently-buffered chunk: bulk-copy all of it.
                    outData.Write(avail);
                    if (modifyMode) sourceData.Position += avail.Length;
                    patchData.Advance(avail.Length);
                    continue;
                }

                if (escIdx > 0)
                {
                    ReadOnlySpan<byte> run = avail.Slice(0, escIdx);
                    outData.Write(run);
                    if (modifyMode) sourceData.Position += run.Length;
                    patchData.Advance(run.Length);
                }

                // Consume the ESC byte itself and inspect what follows -- same logic as
                // the original byte-by-byte version, just fed from the buffered reader.
                patchData.Advance(1);
                const int readChar = (int)JojoOpcode.OPERATION_ESC;
                int nextChar = patchData.ReadByte();

                if (nextChar == (int)JojoOpcode.OPERATION_ESC)
                {
                    //its <esc><esc>.
                    outData.WriteByte((byte)nextChar);
                    if (modifyMode)
                    {
                        sourceData.Position++;
                    }
                }
                else if (isOpcode(nextChar))
                {
                    //its <esc> <opcode>
                    return nextChar; // loop func will process this as the next opcode
                }
                else
                {
                    //it's <esc>... nothing... this shouldn't be possible but maybe some sort of edge case.
                    //(also covers nextChar == EOF, i.e. a lone ESC at the very end of a malformed
                    //patch stream, matching the original byte-by-byte implementation's behavior.)
                    outData.WriteByte((byte)readChar);
                    outData.WriteByte((byte)nextChar);

                    if (modifyMode)
                    {
                        sourceData.Position += 2;
                    }
                }
            }
        }

        /// <summary>
        /// Advances the pointers forward or backwards as the source and destination file data is assumed to be the same.
        /// Data is copied from source data if backwards is false.
        /// </summary>
        /// <param name="sourceData">The source file stream</param>
        /// <param name="patchData">The patch data stream</param>
        /// <param name="outData">The output file stream</param>
        /// <param name="backwards">If this is a backtrace (BKT) opcode. If false, this is an EQL opcode and sourcedata will be copied to the outstream for the read length</param>
        private static void processEqlBktOpcode(Stream sourceData, PatchReader patchData, OutputWriter outData, bool backwards)
        {
            var length = getJLength(patchData);
            if (backwards)
            {
                sourceData.Position -= length;
            }
            else
            {
                //copy
                outData.CopyFrom(sourceData, length);
            }
        }

        public static bool isOpcode(int code)
        {
            return code >= 0xA2 && code <= 0xA6;
        }

        /// <summary>
        /// Copies the inputstream to the outputstream, for the specified amount of bytes
        /// </summary>
        /// <param name="input">Stream to copy from</param>
        /// <param name="output">Stream to copy to</param>
        /// <param name="bytes">The number of bytes to copy</param>
        public static void CopyToEx(Stream input, Stream output, int bytes)
        {
            var buffer = new byte[32768];
            int read;
            while (bytes > 0 &&
                   (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0)
            {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }
    }
}
