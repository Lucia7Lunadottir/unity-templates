using UnityEngine;

public class DangerSource : MonoBehaviour
{
    [SerializeField] private float _radius = 5f; // ������ ������
    [SerializeField] private LayerMask _enemyLayer; // ���� ������
    [SerializeField] private PlayerAttack _playerAttack;

    private void OnEnable()
    {
        _playerAttack.attacked += EmitDanger;
    }

    private void OnDisable()
    {
        _playerAttack.attacked -= EmitDanger;
    }
    // ������ ���� ����� � ������ ����� (����� Animation Event ��� Input)
    public void EmitDanger(GameObject targetObject)
    {
        // ������� ���� ������ � �������
        Collider[] hits = Physics.OverlapSphere(transform.position, _radius, _enemyLayer);

        foreach (var hit in hits)
        {
            // ���������, ���� �� � ��� ��������� "�������"
            if (hit.TryGetComponent(out ICombatReactor reactor))
            {
                // �������: "������! ������ �� ����!"
                reactor.OnDangerSensed(targetObject.transform);
            }
        }
    }

    // ��� ������� � ���������
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _radius);
    }
}
