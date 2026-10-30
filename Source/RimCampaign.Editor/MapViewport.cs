using System;

namespace RimCampaign.Editor
{
    // Coordinates are in map cells, measured from the top left of the canvas.
    internal sealed class MapViewport
    {
        string mapId;
        public float Width { get; private set; }
        public float Height { get; private set; }
        public float ViewWidth { get; private set; }
        public float ViewHeight { get; private set; }
        public float Zoom { get; private set; } = 7;
        public float X { get; private set; }
        public float Y { get; private set; }
        public float CenterX => (ViewWidth / 2 - X) / Zoom;
        public float CenterY => (ViewHeight / 2 - Y) / Zoom;
        public float VisibleLeft => Clamp(-X / Zoom, 0, Width);
        public float VisibleTop => Clamp(-Y / Zoom, 0, Height);
        public float VisibleRight => Clamp((ViewWidth - X) / Zoom, 0, Width);
        public float VisibleBottom => Clamp((ViewHeight - Y) / Zoom, 0, Height);
        static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));

        public void Reset() { mapId = null; Zoom = 7; }

        public void Configure(string id, int width, int height, float viewWidth, float viewHeight)
        {
            bool changedMap = id != mapId || Width != width || Height != height;
            float cx = CenterX, cy = CenterY;
            mapId = id; Width = width; Height = height;
            ViewWidth = Math.Max(1, viewWidth); ViewHeight = Math.Max(1, viewHeight);
            if (changedMap) Center(); else LookAt(cx, cy);
        }

        public void Center() => LookAt(Width / 2, Height / 2);
        public void Fit()
        {
            Zoom = Clamp(Math.Min(Math.Max(1, ViewWidth - 24) / Width, Math.Max(1, ViewHeight - 24) / Height), .1f, 32);
            Center();
        }
        public void LookAt(float x, float y) => SetOffset(ViewWidth / 2 - x * Zoom, ViewHeight / 2 - y * Zoom);
        public void SetOffset(float x, float y)
        {
            X = Width * Zoom <= ViewWidth ? (ViewWidth - Width * Zoom) / 2 : Clamp(x, ViewWidth - Width * Zoom, 0);
            Y = Height * Zoom <= ViewHeight ? (ViewHeight - Height * Zoom) / 2 : Clamp(y, ViewHeight - Height * Zoom, 0);
        }
        public void ZoomAt(float wheelDelta, float x, float y)
        {
            float mapX = (x - X) / Zoom, mapY = (y - Y) / Zoom;
            Zoom = Clamp(Zoom * (float)Math.Pow(1.15, -wheelDelta), .1f, 32);
            SetOffset(x - mapX * Zoom, y - mapY * Zoom);
        }
    }
}
