namespace Gleam.Engine.Capture;

public static class CameraFactory
{
    public static ICameraCapture CreateDefault()
    {
        return new FlashCapCameraCapture();
    }
}