using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

[Serializable]
public class CharacterSelect
{
    public int AllowedCharacters;
    public List<Character> Characters;
    public ExpansionFlags Expansions;

    [Serializable]
    public class Character
    {
        public int Id;
        public string Name;

        public void Select()
        {
            Client.CharacterName = Name;
            Client.SelectCharacter(Id);
        }
    }
}