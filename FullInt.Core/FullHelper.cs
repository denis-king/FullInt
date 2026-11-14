using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace FullInt.Core
{
    public static class FullHelper
    {
        // Max buffer size taken from the stack; larger buffers go to the heap.
        // Buffers sized by the input must never be stackalloc'ed unbounded:
        // a stack overflow cannot be caught and kills the process.
        public const int StackAllocLimitWords = 1024;            // 8 KB
        public const int StackAllocLimitBytes = 8 * 1024;        // 8 KB

        public static bool IsZero(ulong[]? value)
        {
            if (value == null) return true;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != 0) return false;
            }
            return true;
        }

        public static int CountZeros(ulong[] value)
        {
            return CountZeros(value.AsSpan());
            
        }
        public static int CountZeros(Span<ulong> value)
        {
            int zeros;
            for (zeros = 0; zeros < value.Length - 1 && value[zeros] == 0; zeros++) { }
            if (zeros == value.Length)
            {
                zeros--;
            }
            return zeros;
        }
		
		// public static FullInt64 DeleteZeros(FullInt64 value)
        // {
            // value.value = DeleteZeros(value.value).ToArray();
            // return value;
        // }

        /// <summary>
        /// The array itself when it has no leading zero words (no allocation, no copy),
        /// otherwise a trimmed copy. Only for arrays the caller owns: the result may share them.
        /// </summary>
        public static ulong[] Trimmed(ulong[] value)
        {
            int zeros = CountZeros(value);
            return zeros == 0 ? value : value.AsSpan(zeros).ToArray();
        }

        /// <summary>
        /// The result array from a work buffer that is either on the stack (heap == null) or the
        /// heap array heap: a stack buffer is copied out once at its trimmed size, a heap buffer is
        /// kept as is unless it has leading zero words.
        /// </summary>
        public static ulong[] ResultArray(Span<ulong> buffer, ulong[]? heap) =>
            heap != null ? Trimmed(heap) : DeleteZeros(buffer).ToArray();

        public static Span<ulong> DeleteZeros(Span<ulong> value)
        {
            //пропуск старших нулей операнда
            int zeros = CountZeros(value);

            if (zeros == 0)
            {
                return value;
            }

            return value.Slice(zeros);
        }
    }
}
