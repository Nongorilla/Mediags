//
// File:    RaceBinarySearches.cs
// Project: Mediags (benchmarker)
// Purpose: Justify using custom binary search over provided Array.BinarySearch.
// Usage:   Result only valid in Release build.
//

using System;
using System.Diagnostics;
using NongFormat;

namespace AppMain
{
    class RaceBinarySearches
    {
        const int reps = 50000;

        static void Main (string[] args)
        {
            long tot0 = 0, tot1 = 0;
            var watch = new Stopwatch();

            for (int reps1 = 0; reps1 < 5; ++reps1)
            {
                // Benchmark the .NET provided binary searcher.
                watch.Reset();
                watch.Start();
                for (int rx = 0; rx < reps; ++rx)
                {
                    for (var ix = 0; ix < Map1252.Length; ++ix)
                    {
                        var pos = Map1252.ArrayBinarySearch (Map1252.At (ix) & 0x7FFFFF00);
                        var fit = Map1252.At (~pos) & 0xFF;
                        Debug.Assert (ix == ~pos);
                    }
                }
                tot0 += watch.ElapsedMilliseconds;

                // Benchmark the custom binary searcher.
                watch.Reset();
                watch.Start();
                for (int rx = 0; rx < reps; ++rx)
                {
                    for (var ix = 0; ix < Map1252.Length; ++ix)
                    {
                        var result = Map1252.To1252Bestfit (Map1252.At (ix) >> 8);
                        Debug.Assert (result == (Map1252.At (ix) & 0xFF));
                    }
                }
                tot1 += watch.ElapsedMilliseconds;
            }

            Console.WriteLine ("Custom binary search change over Array.BinarySearch = " + 100f * (tot0 - tot1) / tot0 + "%");

            /* Output:

            Custom binary search change over Array.BinarySearch = 32.51122%

            */
        }
    }
}
