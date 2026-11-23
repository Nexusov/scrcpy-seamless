package com.genymobile.scrcpy.video;

/** Numeric encoder-size boundary; Android remains the production authority. */
public interface VideoSizeCapabilities {
    int getWidthAlignment();

    int getHeightAlignment();

    int getMinWidth();

    int getMaxWidth();

    int getMinHeight();

    int getMaxHeight();

    int getMaxWidthForHeight(int height);

    int getMaxHeightForWidth(int width);

    boolean isSizeSupported(int width, int height);
}
