using System.Collections.Generic;
using System.Linq;
using PG.MenuManagement;
using UnityEngine;

namespace PG.BuffSystem
{
    public class ActiveBuffsManager : MonoBehaviour
    {
        [SerializeField] private BuffManager _buffManager;
        [SerializeField] private Transform _cellContainer;
        [SerializeField] private ActiveBuffCell _cellPrefab;

        private void OnEnable()
        {
            _buffManager.OnBuffAdded += DisplayBuffsAdded;
            _buffManager.OnBuffRemoved += DisplayBuffsRemoved;
        }

        private void OnDisable()
        {
            _buffManager.OnBuffAdded -= DisplayBuffsAdded;
            _buffManager.OnBuffRemoved -= DisplayBuffsRemoved;
        }

        void DisplayBuffsAdded(Buff buff, float duration)
        {
            DisplayCells();
        }
        void DisplayBuffsRemoved(Buff buff)
        {
            DisplayCells();
        }
        void DisplayCells()
        {
            List<Buff> buffs = _buffManager.GetActiveBuffs();
            
            if (_cellContainer.childCount < buffs.Count)
            {
                for (int i = 0; i < buffs.Count; i++)
                {
                    ActiveBuffCell activeBuffCell = Instantiate(_cellPrefab, _cellContainer);
                    activeBuffCell.Initialize(buffs[i], _buffManager);
                }
            }
            else
            {
                if (_cellContainer.childCount > 0)
                {
                    for (int i = _cellContainer.childCount - 1; i >= buffs.Count; i--)
                    {
                        GameObject cellObject = _cellContainer.GetChild(i).gameObject;
                        if (cellObject.TryGetComponent(out UIShowHide cell))
                        {
                            cell.Hide(() => Destroy(cellObject));
                        }
                        else
                        {
                            Destroy(cellObject);
                        }
                    
                    }
                }
            }
        }
    }
}