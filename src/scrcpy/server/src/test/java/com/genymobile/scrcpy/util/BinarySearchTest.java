package com.genymobile.scrcpy.util;

import org.junit.Assert;
import org.junit.Test;

public class BinarySearchTest {
    @Test
    public void findsEveryBoundaryIncludingEmptyAndSingletonIntervals() {
        for (int low = 0; low < 8; ++low) {
            for (int high = 0; high < 8; ++high) {
                for (int threshold = -1; threshold < 9; ++threshold) {
                    int acceptedThrough = threshold;
                    int expected = high < low ? low - 1 : Math.max(low - 1, Math.min(high, threshold));
                    Assert.assertEquals(expected, BinarySearch.findHighestTrue(low, high, value -> value <= acceptedThrough));
                }
            }
        }
    }

    @Test
    public void predicateWorkRemainsLogarithmicForTheCodecDimensionRange() {
        int[] calls = {0};
        int result = BinarySearch.findHighestTrue(0, 65535, value -> {
            ++calls[0];
            return value <= 12345;
        });
        Assert.assertEquals(12345, result);
        // One fast-path probe plus at most seventeen interval probes.
        Assert.assertTrue(calls[0] <= 18);
    }
}
