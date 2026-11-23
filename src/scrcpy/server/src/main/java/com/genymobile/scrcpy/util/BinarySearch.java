package com.genymobile.scrcpy.util;

public final class BinarySearch {
    private BinarySearch() {
        // not instantiable
    }

    /** Keep physical-display failure recovery compatible with Android API 21. */
    public interface IntPredicate {
        boolean test(int value);
    }

    /**
     * Find the highest value for which the predicate is true.
     * <p>
     * This assumes that the predicate is monotonic:
     * - if it returns {@code false} for a given value, it must return {@code false} for higher values, or equivalently
     * - if it returns {@code true} for a given value, it must return {@code true} for lower values.
     *
     * @param low       the lowest value
     * @param high      the highest value
     * @param predicate the predicate
     * @return the highest value for which {@code predicate.test(value)} is {@code true}, or {@code low - 1} if not found.
     */
    public static int findHighestTrue(int low, int high, IntPredicate predicate) {

        if (low <= high) {
            // Fast path

            if (predicate.test(high)) {
                return high;
            }
            --high;
        }

        int result = low - 1; // low-1 means "not found"
        while (low <= high) {
            int mid = low + (high - low + 1) / 2;

            if (predicate.test(mid)) {
                result = mid;

                // predicate holds, go right
                low = mid + 1;
            } else {
                // predicate false, go left
                high = mid - 1;
            }
        }

        return result;
    }
}
