using System;
using UnityEngine;

namespace PG.BuffManagement
{
    [Serializable]
    public struct BuffModifier
    {
        [SerializeField] public BuffStatType    statType;
        [SerializeField] public BuffModifierType modifierType;
        [SerializeField] public float            value;
    }
}
