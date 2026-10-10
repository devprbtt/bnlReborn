using System;

// Shared with the server. Cell kinds: 0 = ordinary, 1 = solid metal, 2 = forcegate.
// Walk voxel faces, including exits that physics rays starting inside a collider miss.
public static class ShotBoundaryTrace
{
    public struct Contact
    {
        public float Fraction;
        public int Axis;
        public int Normal;
        public bool GateExit;
    }

    public static bool Trace(float x, float y, float z, float endX, float endY, float endZ,
        Func<int, int, int, int> kind, out Contact contact)
    {
        contact = default(Contact);
        Axis ax = new Axis(x, endX), ay = new Axis(y, endY), az = new Axis(z, endZ);
        int budget = 3 + ax.Crossings + ay.Crossings + az.Crossings;
        int previous = kind(ax.Cell, ay.Cell, az.Cell);
        if (previous == 1 && (x != endX || y != endY || z != endZ))
        {
            int a = Math.Abs(endX - x) >= Math.Abs(endY - y) && Math.Abs(endX - x) >= Math.Abs(endZ - z)
                ? 0 : Math.Abs(endY - y) >= Math.Abs(endZ - z) ? 1 : 2;
            contact = new Contact { Axis = a, Normal = -(a == 0 ? ax.Step : a == 1 ? ay.Step : az.Step) };
            return true;
        }
        while (budget-- > 0)
        {
            int a = ax.Next <= ay.Next && ax.Next <= az.Next ? 0 : ay.Next <= az.Next ? 1 : 2;
            ref Axis axis = ref (a == 0 ? ref ax : ref (a == 1 ? ref ay : ref az));
            float t = axis.Next;
            if (float.IsInfinity(t) || t > 1f) return false;
            axis.Cell += axis.Step;
            int entered = kind(ax.Cell, ay.Cell, az.Cell);
            bool exit = previous == 2 && entered != 2;
            if (exit || entered == 1)
            {
                contact = new Contact { Fraction = t, Axis = a,
                    Normal = exit ? axis.Step : -axis.Step, GateExit = exit };
                return true;
            }
            previous = entered;
            axis.Next += axis.Stride;
        }
        return false;
    }

    private struct Axis
    {
        public int Cell, Step, Crossings;
        public float Next, Stride;
        public Axis(float start, float end)
        {
            Cell = (int)Math.Floor(start);
            float delta = end - start;
            Step = Math.Sign(delta);
            Crossings = Math.Abs((int)Math.Floor(end) - Cell);
            Next = Step == 0 ? float.PositiveInfinity : (Cell + (Step > 0 ? 1 : 0) - start) / delta;
            Stride = Step == 0 ? float.PositiveInfinity : Math.Abs(1f / delta);
        }
    }
}
