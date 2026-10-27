using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tyap_2
{
    /// <summary>
    /// Одна строка протокола работы магазинного автомата.
    /// </summary>
    public class ParseRule
    {
        public int Step { get; set; }              // номер такта
        public string NonTerminal { get; set; }    // символ на вершине стека
        public string Production { get; set; }     // действие автомата
        public string Token { get; set; }          // текущий входной символ
        public int Line { get; set; }
        public int Column { get; set; }
        public string Stack { get; set; }          // содержимое стека (вершина слева)

        public ParseRule(int step, string nonTerminal, string production,
                         string token, int line, int column)
        {
            Step = step;
            NonTerminal = nonTerminal;
            Production = production;
            Token = token;
            Line = line;
            Column = column;
            Stack = "";
        }
    }

    public class Production
    {
        public int Number { get; set; }
        public string Left { get; set; }
        public string[] Right { get; set; }
        public bool IsEpsilon { get { return Right.Length == 0; } }

        public override string ToString()
        {
            return Left + " -> " + (IsEpsilon ? "ε" : string.Join(" ", Right));
        }
    }

    /// <summary>
    /// Синтаксический анализатор — детерминированный нисходящий магазинный автомат
    /// для грамматики типа LL(1).
    ///
    /// M = ({q}, Σ, Γ, δ, q, I$), где
    ///   δ(q, a, A) = (q, α)   если A -> α и a ∈ ВЫБОР(A -> α)  (замена вершины стека)
    ///   δ(q, a, a) = (q, ε)   сдвиг: терминал на вершине совпал со входным
    ///   δ(q, $, $) = допуск
    /// Таблица переходов строится автоматически по функциям ПЕРВ и СЛЕД.
    ///
    /// Обозначения терминалов: E — идентификатор, M — константа.
    /// </summary>
    public class Parser
    {
        private const string Eps = "ε";
        private const string EndMark = "$";
        private const string StartSymbol = "I";

        // Грамматика после устранения левой рекурсии и факторизации (LL(1)).
        private static readonly string[] GrammarText =
        {
            "I -> A B",
            "A -> VAR D : INTEGER ;",
            "B -> BEGIN C END",
            "C -> S C'",
            "C' -> C",
            "C' -> ε",
            "S -> F",
            "S -> READ ( D ) ;",
            "S -> WRITE ( D ) ;",
            "S -> FOR E = G TO G DO C END_FOR ;",
            "F -> E = G ;",
            "D -> E D'",
            "D' -> , D",
            "D' -> ε",
            "G -> H J",
            "G -> J",
            "H -> -",
            "J -> P J'",
            "J' -> L P J'",
            "J' -> ε",
            "P -> ( G )",
            "P -> K",
            "K -> E",
            "K -> M",
            "L -> +",
            "L -> -",
            "L -> *"
        };

        public static readonly List<Production> Productions = new List<Production>();
        public static readonly HashSet<string> NonTerminals = new HashSet<string>();
        public static readonly List<string> NonTerminalOrder = new List<string>();
        public static readonly List<string> TerminalOrder = new List<string>();
        public static readonly Dictionary<string, HashSet<string>> First = new Dictionary<string, HashSet<string>>();
        public static readonly Dictionary<string, HashSet<string>> Follow = new Dictionary<string, HashSet<string>>();
        public static readonly Dictionary<string, Dictionary<string, Production>> Table =
            new Dictionary<string, Dictionary<string, Production>>();
        public static readonly List<string> Conflicts = new List<string>();

        static Parser()
        {
            // --- разбор текста грамматики ---
            int n = 1;
            foreach (string line in GrammarText)
            {
                string[] parts = line.Split(new[] { "->" }, StringSplitOptions.None);
                string left = parts[0].Trim();
                string rhs = parts[1].Trim();
                string[] right = rhs == Eps
                    ? new string[0]
                    : rhs.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                Productions.Add(new Production { Number = n++, Left = left, Right = right });
                if (NonTerminals.Add(left)) NonTerminalOrder.Add(left);
            }
            foreach (Production p in Productions)
                foreach (string s in p.Right)
                    if (!NonTerminals.Contains(s) && !TerminalOrder.Contains(s))
                        TerminalOrder.Add(s);

            foreach (string nt in NonTerminalOrder)
            {
                First[nt] = new HashSet<string>();
                Follow[nt] = new HashSet<string>();
            }

            // --- ПЕРВ ---
            bool changed;
            do
            {
                changed = false;
                foreach (Production p in Productions)
                    foreach (string x in FirstOfSeq(p.Right, 0))
                        if (First[p.Left].Add(x)) changed = true;
            } while (changed);

            // --- СЛЕД ---
            Follow[StartSymbol].Add(EndMark);
            do
            {
                changed = false;
                foreach (Production p in Productions)
                {
                    for (int i = 0; i < p.Right.Length; i++)
                    {
                        string s = p.Right[i];
                        if (!NonTerminals.Contains(s)) continue;

                        HashSet<string> f = FirstOfSeq(p.Right, i + 1);
                        foreach (string x in f)
                            if (x != Eps && Follow[s].Add(x)) changed = true;

                        if (f.Contains(Eps))
                            foreach (string x in Follow[p.Left].ToList())
                                if (Follow[s].Add(x)) changed = true;
                    }
                }
            } while (changed);

            // --- таблица переходов ---
            foreach (string nt in NonTerminalOrder)
                Table[nt] = new Dictionary<string, Production>();

            foreach (Production p in Productions)
            {
                HashSet<string> sel = FirstOfSeq(p.Right, 0);
                HashSet<string> terms = new HashSet<string>(sel.Where(x => x != Eps));
                if (sel.Contains(Eps))
                    foreach (string f in Follow[p.Left]) terms.Add(f);

                foreach (string t in terms)
                {
                    if (Table[p.Left].ContainsKey(t))
                        Conflicts.Add($"Конфликт в ячейке [{p.Left}, {t}]: правила {Table[p.Left][t].Number} и {p.Number}");
                    else
                        Table[p.Left][t] = p;
                }
            }
        }

        private static HashSet<string> FirstOfSeq(string[] seq, int start)
        {
            HashSet<string> res = new HashSet<string>();
            bool allEps = true;
            for (int i = start; i < seq.Length; i++)
            {
                string s = seq[i];
                if (!NonTerminals.Contains(s))
                {
                    res.Add(s);
                    allEps = false;
                    break;
                }
                foreach (string f in First[s])
                    if (f != Eps) res.Add(f);
                if (!First[s].Contains(Eps))
                {
                    allEps = false;
                    break;
                }
            }
            if (allEps) res.Add(Eps);
            return res;
        }

        // ------------------------------------------------------------------
        //                          РАБОТА АВТОМАТА
        // ------------------------------------------------------------------

        private readonly List<Token> tokens;
        private int pos;
        private int step;
        private int lastErrorPos;

        public List<LexError> Errors { get; private set; }
        public List<ParseRule> Rules { get; private set; }

        public Parser(List<Token> tokens)
        {
            this.tokens = tokens;
            pos = 0;
            step = 1;
            lastErrorPos = -1;
            Errors = new List<LexError>();
            Rules = new List<ParseRule>();
        }

        public bool Parse()
        {
            List<string> stack = new List<string> { EndMark, StartSymbol };   // вершина — последний элемент

            while (true)
            {
                string top = stack[stack.Count - 1];
                Token cur = Current();
                string a = TerminalName(cur);

                // ---- дно стека ----
                if (top == EndMark)
                {
                    if (a == EndMark)
                    {
                        Log(stack, top, "Допуск: стек пуст, вход исчерпан", cur);
                    }
                    else
                    {
                        Log(stack, top, "Ошибка: лишние символы", cur);
                        ReportError(cur, "Лишние символы после конца программы");
                    }
                    break;
                }

                // ---- терминал на вершине: сдвиг ----
                if (!NonTerminals.Contains(top))
                {
                    if (top == a)
                    {
                        Log(stack, top, "Сдвиг (символы совпали)", cur);
                        stack.RemoveAt(stack.Count - 1);
                        Advance();
                    }
                    else
                    {
                        Log(stack, top, "Ошибка: символ не совпал, терминал снят со стека", cur);
                        ReportError(cur, $"Ожидался символ '{TermLabel(top)}', обнаружено '{TokenLabel(cur)}'");
                        stack.RemoveAt(stack.Count - 1);
                    }
                    continue;
                }

                // ---- нетерминал на вершине: переход по таблице ----
                Production p;
                if (Table[top].TryGetValue(a, out p))
                {
                    Log(stack, top, p.ToString(), cur);
                    stack.RemoveAt(stack.Count - 1);
                    for (int i = p.Right.Length - 1; i >= 0; i--)
                        stack.Add(p.Right[i]);
                }
                else
                {
                    string expected = string.Join(", ", Table[top].Keys.Select(TermLabel));
                    ReportError(cur, $"Неожиданный символ '{TokenLabel(cur)}'. Ожидалось: {expected}");

                    // нейтрализация ошибки: нетерминал снимается со стека, вход не трогаем
                    Log(stack, top, "Ошибка: нет перехода, нетерминал снят со стека", cur);
                    stack.RemoveAt(stack.Count - 1);
                }
            }

            return Errors.Count == 0;
        }

        // ------------------------------------------------------------------
        //                          ВСПОМОГАТЕЛЬНЫЕ
        // ------------------------------------------------------------------

        private Token Current()
        {
            if (pos < tokens.Count) return tokens[pos];
            return tokens.Count > 0
                ? tokens[tokens.Count - 1]
                : new Token(TokenType.EOF, "", 1, 1);
        }

        private void Advance()
        {
            if (pos < tokens.Count - 1) pos++;
        }

        private static string TerminalName(Token t)
        {
            switch (t.Type)
            {
                case TokenType.VAR: return "VAR";
                case TokenType.BEGIN: return "BEGIN";
                case TokenType.END: return "END";
                case TokenType.INTEGER: return "INTEGER";
                case TokenType.READ: return "READ";
                case TokenType.WRITE: return "WRITE";
                case TokenType.FOR: return "FOR";
                case TokenType.TO: return "TO";
                case TokenType.DO: return "DO";
                case TokenType.END_FOR: return "END_FOR";
                case TokenType.IDENTIFIER: return "E";
                case TokenType.CONSTANT: return "M";
                case TokenType.PLUS: return "+";
                case TokenType.MINUS: return "-";
                case TokenType.MULTIPLY: return "*";
                case TokenType.ASSIGN: return "=";
                case TokenType.COLON: return ":";
                case TokenType.SEMICOLON: return ";";
                case TokenType.COMMA: return ",";
                case TokenType.LEFT_PAREN: return "(";
                case TokenType.RIGHT_PAREN: return ")";
                case TokenType.EOF: return EndMark;
                default: return "?";
            }
        }

        private static string TermLabel(string t)
        {
            if (t == "E") return "идентификатор";
            if (t == "M") return "константа";
            if (t == EndMark) return "конец программы";
            return t;
        }

        private static string TokenLabel(Token t)
        {
            if (t.Type == TokenType.EOF) return "$";
            return string.IsNullOrEmpty(t.Value) ? t.Type.ToString() : t.Value;
        }

        private void Log(List<string> stack, string top, string action, Token cur)
        {
            ParseRule r = new ParseRule(step++, top, action, TokenLabel(cur), cur.Line, cur.Column);
            r.Stack = string.Join(" ", Enumerable.Reverse(stack));
            Rules.Add(r);
        }

        private void ReportError(Token cur, string message)
        {
            if (pos == lastErrorPos) return;      // одна ошибка на один входной символ
            if (Errors.Count >= 20) return;
            lastErrorPos = pos;
            Errors.Add(new LexError(Errors.Count + 1, "Синтаксическая", cur.Line, cur.Column, message));
        }

        // ------------------------------------------------------------------
        //            ОПИСАНИЕ АВТОМАТА (для вкладки и пояснительной записки)
        // ------------------------------------------------------------------

        public static string DescribeAutomaton()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("МАГАЗИННЫЙ АВТОМАТ (детерминированный, нисходящий, LL(1))");
            sb.AppendLine();
            sb.AppendLine("M = (Q, Σ, Γ, δ, q0, Z0)");
            sb.AppendLine("  Q  = { q }");
            sb.AppendLine("  Σ  = { " + string.Join(", ", TerminalOrder) + " }   (E — идентификатор, M — константа)");
            sb.AppendLine("  Γ  = Σ ∪ N ∪ { $ }");
            sb.AppendLine("  Z0 = I (над маркером дна $)");
            sb.AppendLine();

            if (Conflicts.Count > 0)
            {
                sb.AppendLine("!!! Грамматика не LL(1):");
                foreach (string c in Conflicts) sb.AppendLine("  " + c);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("Конфликтов в таблице нет — грамматика LL(1).");
                sb.AppendLine();
            }

            sb.AppendLine("ПРАВИЛА ГРАММАТИКИ");
            foreach (Production p in Productions)
                sb.AppendLine($"  {p.Number,2}) {p}");
            sb.AppendLine();

            sb.AppendLine("ФУНКЦИЯ ПЕРВ");
            foreach (string nt in NonTerminalOrder)
                sb.AppendLine($"  ПЕРВ({nt}) = {{ {string.Join(", ", OrderedSet(First[nt]))} }}");
            sb.AppendLine();

            sb.AppendLine("ФУНКЦИЯ СЛЕД");
            foreach (string nt in NonTerminalOrder)
                sb.AppendLine($"  СЛЕД({nt}) = {{ {string.Join(", ", OrderedSet(Follow[nt]))} }}");
            sb.AppendLine();

            sb.AppendLine("ФУНКЦИЯ ПЕРЕХОДОВ δ(q, входной символ, вершина стека) = (q, новое содержимое вершины)");
            foreach (string nt in NonTerminalOrder)
            {
                foreach (string t in OrderedSet(Table[nt].Keys))
                {
                    Production p = Table[nt][t];
                    string rhs = p.IsEpsilon ? "ε" : string.Join(" ", p.Right);
                    sb.AppendLine($"  δ(q, {t}, {nt}) = (q, {rhs})    [правило {p.Number}]");
                }
            }
            sb.AppendLine();
            sb.AppendLine("  Сдвиг:");
            foreach (string t in TerminalOrder)
                sb.AppendLine($"  δ(q, {t}, {t}) = (q, ε)");
            sb.AppendLine();
            sb.AppendLine("  Допуск:");
            sb.AppendLine("  δ(q, $, $) = допуск");
            return sb.ToString();
        }

        private static IEnumerable<string> OrderedSet(IEnumerable<string> set)
        {
            List<string> items = set.ToList();
            return items.OrderBy(x =>
            {
                int i = TerminalOrder.IndexOf(x);
                if (x == EndMark) return 1000;
                if (x == Eps) return 1001;
                return i < 0 ? 999 : i;
            });
        }
    }
}