using System.Numerics;
using SETUE.Core;

namespace SETUE.Components
{
    public struct Position : IComponent
    {
        public float X, Y, Z;
    }

    public struct Mesh : IComponent
    {
        public string MeshId;
        public string PipelineId;
        public int Layer;
    }

    public struct Camera : IComponent
    {
        public float PosX, PosY, PosZ;
        public float PivotX, PivotY, PivotZ;
        public float Fov, Near, Far;
        public bool InvertX, InvertY;
    }

    public struct Panel : IComponent
    {
        public string Id;
        public int Left, Right, Top, Bottom;
        public float R, G, B, Alpha;
        public bool Visible;
        public int Layer;
    }

    public struct Text : IComponent
    {
        public string Content;
        public string FontId;
        public int X, Y;
        public float R, G, B, Alpha;
    }
}

public struct Scale : IComponent
{
    public float Value;
}

public struct Rotation : IComponent
{
    public float X, Y, Z;
}

public struct Color : IComponent
{
    public float R, G, B;
}
