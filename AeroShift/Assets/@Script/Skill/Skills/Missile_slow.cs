using System.Collections.Generic;
using UnityEngine;

public class Missile_slow : MonoBehaviour
{
    [SerializeField] float duration;

    private bool isActive;
    private bool hasUsed;

    public void Activate(float speed)
    {
        
    }

    public void ResetSkill()
    {
        StopAllCoroutines();
        
    }

    public void UseMissileSlow()
    {

    }
}
