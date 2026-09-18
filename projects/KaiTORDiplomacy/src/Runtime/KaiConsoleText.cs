using System.Text;

namespace KaiTOR.Diplomacy.Runtime;

internal static class KaiConsoleText
{
    public static string Safe(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length * 2);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case 'А': sb.Append("A"); break; case 'а': sb.Append("a"); break;
                case 'Б': sb.Append("B"); break; case 'б': sb.Append("b"); break;
                case 'В': sb.Append("V"); break; case 'в': sb.Append("v"); break;
                case 'Г': sb.Append("G"); break; case 'г': sb.Append("g"); break;
                case 'Д': sb.Append("D"); break; case 'д': sb.Append("d"); break;
                case 'Е': case 'Ё': sb.Append("E"); break; case 'е': case 'ё': sb.Append("e"); break;
                case 'Ж': sb.Append("Zh"); break; case 'ж': sb.Append("zh"); break;
                case 'З': sb.Append("Z"); break; case 'з': sb.Append("z"); break;
                case 'И': case 'Й': sb.Append("I"); break; case 'и': case 'й': sb.Append("i"); break;
                case 'К': sb.Append("K"); break; case 'к': sb.Append("k"); break;
                case 'Л': sb.Append("L"); break; case 'л': sb.Append("l"); break;
                case 'М': sb.Append("M"); break; case 'м': sb.Append("m"); break;
                case 'Н': sb.Append("N"); break; case 'н': sb.Append("n"); break;
                case 'О': sb.Append("O"); break; case 'о': sb.Append("o"); break;
                case 'П': sb.Append("P"); break; case 'п': sb.Append("p"); break;
                case 'Р': sb.Append("R"); break; case 'р': sb.Append("r"); break;
                case 'С': sb.Append("S"); break; case 'с': sb.Append("s"); break;
                case 'Т': sb.Append("T"); break; case 'т': sb.Append("t"); break;
                case 'У': sb.Append("U"); break; case 'у': sb.Append("u"); break;
                case 'Ф': sb.Append("F"); break; case 'ф': sb.Append("f"); break;
                case 'Х': sb.Append("Kh"); break; case 'х': sb.Append("kh"); break;
                case 'Ц': sb.Append("Ts"); break; case 'ц': sb.Append("ts"); break;
                case 'Ч': sb.Append("Ch"); break; case 'ч': sb.Append("ch"); break;
                case 'Ш': sb.Append("Sh"); break; case 'ш': sb.Append("sh"); break;
                case 'Щ': sb.Append("Shch"); break; case 'щ': sb.Append("shch"); break;
                case 'Ъ': case 'ъ': case 'Ь': case 'ь': break;
                case 'Ы': sb.Append("Y"); break; case 'ы': sb.Append("y"); break;
                case 'Э': sb.Append("E"); break; case 'э': sb.Append("e"); break;
                case 'Ю': sb.Append("Yu"); break; case 'ю': sb.Append("yu"); break;
                case 'Я': sb.Append("Ya"); break; case 'я': sb.Append("ya"); break;
                default:
                    sb.Append(ch <= 127 ? ch : '?');
                    break;
            }
        }

        return sb.ToString();
    }
}
