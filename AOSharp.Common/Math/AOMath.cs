namespace AOSharp.Common;

public static class AOMath
{
    public static int SellPrice(int value, int compLit, float shopModifier = 4f)
    {
        var clModifier = compLit / 40;
        return (int)(value * shopModifier * (100 + clModifier) / 2500);
    }
}