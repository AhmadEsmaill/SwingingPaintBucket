using UnityEngine;

public class SPHParticle
{
    public Vector2 position;
    public Vector2 velocity;
    public Vector2 acceleration;
    public float density;
    public float pressure;

    public SPHParticle(Vector2 pos)
    {
        position = pos;
    }
}
