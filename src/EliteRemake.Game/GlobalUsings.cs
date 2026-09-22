// The presentation layer works with MonoGame's vector types; the simulation uses System.Numerics.
// These aliases keep the two from fighting over the same names.
global using Vector2 = Microsoft.Xna.Framework.Vector2;
global using Vector3 = Microsoft.Xna.Framework.Vector3;
global using Matrix = Microsoft.Xna.Framework.Matrix;
global using Color = Microsoft.Xna.Framework.Color;
