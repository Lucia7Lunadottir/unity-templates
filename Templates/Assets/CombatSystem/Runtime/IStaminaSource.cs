// Выносливость игрока для рывка и барьера. Реализует компонент игры (например, StaminaController); пакет про него не знает
public interface IStaminaSource
{
    // Выносливость на нуле, пока не восстановится, тратить нельзя
    bool isResting { get; }

    // Потратить выносливость
    void Spend(float amount);
}
