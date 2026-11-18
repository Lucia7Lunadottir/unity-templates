// То, у чего ночью и днём меняется время устойчивости к холоду (температура игрока); пакет про неё не знает
public interface IResilienceTarget
{
    float resilienceTime { get; set; }
}
