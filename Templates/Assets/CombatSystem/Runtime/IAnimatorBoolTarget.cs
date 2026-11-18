// Что угодно с bool-параметрами по имени (например, VAT-аниматор игры); пакет про него не знает
public interface IAnimatorBoolTarget
{
    void SetBool(string parameterName, bool value);
}
