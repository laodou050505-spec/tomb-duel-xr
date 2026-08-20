namespace Guandan.Game
{
    public enum PointerSource
    {
        Desktop,
        LeftController,
        RightController,
    }

    public interface IWorldInteractable
    {
        void Interact(PointerSource source);
    }

    public enum GameAction
    {
        Play,
        Pass,
        Hint,
        EnterTreasure,
        SteadyRoute,
        RiskyRoute,
        ContinueHand,
        RestartMatch,
    }

    public enum SocialPropType
    {
        Flower,
        Tomato,
    }
}
