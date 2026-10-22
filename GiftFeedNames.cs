internal static class GiftFeedNames
{
    internal static string BallName(string type, int count)
    {
        switch (type)
        {
            case "Orange": return count == 1 ? "Bola Laranja" : "Bolas Laranjas";
            case "Yellow": return count == 1 ? "Bola Amarela" : "Bolas Amarelas";
            case "Green": return count == 1 ? "Bola Verde" : "Bolas Verdes";
            case "Pink": return count == 1 ? "Bola Roxa" : "Bolas Roxas";
            case "Inferno": return count == 1 ? "Inferno Ball" : "Inferno Balls";
            default: return count == 1 ? "Bola" : "Bolas";
        }
    }

    internal static string GiftName(string name, int count)
    {
        if (count == 1) return name == "Rose" ? "Rosa" : name;
        switch (name)
        {
            case "Rose": case "Rosa": return "Rosas";
            case "Finger Heart": return "Finger Hearts";
            case "Hand Heart": return "Hand Hearts";
            case "Heart Me": case "TikTok": case "GG": return name;
            case "Perfume": return "Perfumes";
            case "Doughnut": return "Doughnuts";
            default: return name + "s";
        }
    }
}
