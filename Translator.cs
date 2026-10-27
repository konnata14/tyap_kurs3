using System;
using System.Collections.Generic;

namespace tyap_2
{
    public enum PolizKind
    {
        PushAddr, PushVar, PushConst,
        Add, Sub, Mul, Neg, LessEq,
        Assign, Read, Write, Jmp, Jz
    }

    public class PolizItem
    {
        public int Index { get; set; }
        public PolizKind Kind { get; set; }
        public string Name { get; set; }
        public int Value { get; set; }
        public int Target { get; set; } = -1;

        public string Text
        {
            get
            {
                switch (Kind)
                {
                    case PolizKind.PushAddr: return "&" + Name;
                    case PolizKind.PushVar: return Name;
                    case PolizKind.PushConst: return Value.ToString();
                    case PolizKind.Add: return "+";
                    case PolizKind.Sub: return "-";
                    case PolizKind.Mul: return "*";
                    case PolizKind.Neg: return "@";
                    case PolizKind.LessEq: return "<=";
                    case PolizKind.Assign: return "=";
                    case PolizKind.Read: return "READ";
                    case PolizKind.Write: return "WRITE";
                    case PolizKind.Jmp: return "JMP " + Target;
                    case PolizKind.Jz: return "JZ " + Target;
                    default: return "?";
                }
            }
        }

        public string Description
        {
            get
            {
                switch (Kind)
                {
                    case PolizKind.PushAddr: return "Адрес переменной в стек";
                    case PolizKind.PushVar: return "Значение переменной в стек";
                    case PolizKind.PushConst: return "Константа в стек";
                    case PolizKind.Add:
                    case PolizKind.Sub:
                    case PolizKind.Mul: return "Бинарная операция";
                    case PolizKind.Neg: return "Унарный минус";
                    case PolizKind.LessEq: return "Сравнение «меньше или равно»";
                    case PolizKind.Assign: return "Присваивание";
                    case PolizKind.Read: return "Ввод значения";
                    case PolizKind.Write: return "Вывод значения";
                    case PolizKind.Jmp: return "Безусловный переход";
                    case PolizKind.Jz: return "Переход, если ложь (0)";
                    default: return "";
                }
            }
        }
    }

    /// <summary>
    /// Семантический анализ + генерация ПОЛИЗ.
    /// Вызывается только после успешного синтаксического разбора.
    /// </summary>
    public class Translator
    {
        public const string WarningType = "Предупреждение";

        private readonly List<Token> tokens;
        private int pos;

        private readonly HashSet<string> declared = new HashSet<string>();
        private readonly HashSet<string> assigned = new HashSet<string>();
        private readonly HashSet<string> warned = new HashSet<string>();
        private readonly List<string> loopVars = new List<string>();
        private int tempCounter;

        public List<PolizItem> Poliz { get; private set; }
        public List<LexError> Errors { get; private set; }
        public int ErrorCount { get; private set; }

        public Translator(List<Token> tokens)
        {
            this.tokens = tokens;
            Poliz = new List<PolizItem>();
            Errors = new List<LexError>();
        }

        public void Translate()
        {
            // A -> VAR D : INTEGER ;
            Skip(TokenType.VAR);
            while (Cur().Type != TokenType.COLON && Cur().Type != TokenType.EOF)
            {
                Token t = Cur();
                if (t.Type == TokenType.IDENTIFIER && !declared.Add(t.Value))
                    Error(t, $"Повторное описание переменной '{t.Value}'");
                Advance();
            }
            Skip(TokenType.COLON);
            Skip(TokenType.INTEGER);
            Skip(TokenType.SEMICOLON);

            // B -> BEGIN C END
            Skip(TokenType.BEGIN);
            Statements();
            Skip(TokenType.END);
        }

        // ---------------------------- операторы ----------------------------

        private void Statements()
        {
            while (true)
            {
                switch (Cur().Type)
                {
                    case TokenType.IDENTIFIER: Assignment(); break;
                    case TokenType.READ: ReadStatement(); break;
                    case TokenType.WRITE: WriteStatement(); break;
                    case TokenType.FOR: ForStatement(); break;
                    default: return;
                }
            }
        }

        private void Assignment()
        {
            Token id = Cur();
            CheckDeclared(id);
            CheckNotLoopVar(id);
            Advance();
            Skip(TokenType.ASSIGN);

            Emit(PolizKind.PushAddr, id.Value);
            Expression();
            Emit(PolizKind.Assign);
            Skip(TokenType.SEMICOLON);

            assigned.Add(id.Value);   // после разбора правой части
        }

        private void ReadStatement()
        {
            Advance();                          // READ
            Skip(TokenType.LEFT_PAREN);
            while (Cur().Type == TokenType.IDENTIFIER)
            {
                Token id = Cur();
                CheckDeclared(id);
                CheckNotLoopVar(id);
                Emit(PolizKind.PushAddr, id.Value);
                Emit(PolizKind.Read);
                assigned.Add(id.Value);
                Advance();
                Skip(TokenType.COMMA);
            }
            Skip(TokenType.RIGHT_PAREN);
            Skip(TokenType.SEMICOLON);
        }

        private void WriteStatement()
        {
            Advance();                          // WRITE
            Skip(TokenType.LEFT_PAREN);
            while (Cur().Type == TokenType.IDENTIFIER)
            {
                Token id = Cur();
                UseVar(id);
                Emit(PolizKind.PushVar, id.Value);
                Emit(PolizKind.Write);
                Advance();
                Skip(TokenType.COMMA);
            }
            Skip(TokenType.RIGHT_PAREN);
            Skip(TokenType.SEMICOLON);
        }

        // FOR I = G1 TO G2 DO C END_FOR ;
        //
        //   &I  G1  =
        //   &$limN  G2  =
        // L: I $limN <= JZ(end)
        //   <тело>
        //   &I I 1 + =
        //   JMP L
        // end:
        private void ForStatement()
        {
            Advance();                          // FOR
            Token cnt = Cur();
            CheckDeclared(cnt);
            if (loopVars.Contains(cnt.Value))
                Error(cnt, $"Переменная '{cnt.Value}' уже является счётчиком внешнего цикла FOR");
            Advance();
            Skip(TokenType.ASSIGN);

            Emit(PolizKind.PushAddr, cnt.Value);
            Expression();
            Emit(PolizKind.Assign);
            assigned.Add(cnt.Value);

            Skip(TokenType.TO);
            string lim = "$lim" + (++tempCounter);
            Emit(PolizKind.PushAddr, lim);
            Expression();
            Emit(PolizKind.Assign);

            Skip(TokenType.DO);

            int start = Poliz.Count;
            Emit(PolizKind.PushVar, cnt.Value);
            Emit(PolizKind.PushVar, lim);
            Emit(PolizKind.LessEq);
            PolizItem jz = Emit(PolizKind.Jz);

            loopVars.Add(cnt.Value);
            Statements();
            loopVars.RemoveAt(loopVars.Count - 1);

            Skip(TokenType.END_FOR);
            Skip(TokenType.SEMICOLON);

            Emit(PolizKind.PushAddr, cnt.Value);
            Emit(PolizKind.PushVar, cnt.Value);
            Emit(PolizKind.PushConst, null, 1);
            Emit(PolizKind.Add);
            Emit(PolizKind.Assign);
            PolizItem jmp = Emit(PolizKind.Jmp);
            jmp.Target = start;
            jz.Target = Poliz.Count;
        }

        // ------------- выражение -> ПОЛИЗ (алгоритм со стеком операций) -------------

        private void Expression()
        {
            var ops = new Stack<PolizKind?>();   // null — открывающая скобка
            int depth = 0;
            bool expectOperand = true;

            while (true)
            {
                Token t = Cur();

                if (expectOperand)
                {
                    if (t.Type == TokenType.MINUS)              // унарный минус по контексту
                    {
                        ops.Push(PolizKind.Neg);
                        Advance();
                    }
                    else if (t.Type == TokenType.LEFT_PAREN)
                    {
                        ops.Push(null);
                        depth++;
                        Advance();
                    }
                    else if (t.Type == TokenType.IDENTIFIER)
                    {
                        UseVar(t);
                        Emit(PolizKind.PushVar, t.Value);
                        Advance();
                        expectOperand = false;
                    }
                    else if (t.Type == TokenType.CONSTANT)
                    {
                        int v;
                        if (!int.TryParse(t.Value, out v))
                        {
                            Error(t, $"Константа {t.Value} выходит за пределы типа INTEGER");
                            v = 0;
                        }
                        Emit(PolizKind.PushConst, null, v);
                        Advance();
                        expectOperand = false;
                    }
                    else break;
                }
                else
                {
                    if (t.Type == TokenType.RIGHT_PAREN && depth > 0)
                    {
                        while (ops.Peek() != null) Emit(ops.Pop().Value);
                        ops.Pop();
                        depth--;
                        Advance();
                    }
                    else if (IsBinary(t.Type))
                    {
                        PolizKind op = ToKind(t.Type);
                        while (ops.Count > 0 && ops.Peek() != null &&
                               Priority(ops.Peek().Value) >= Priority(op))
                            Emit(ops.Pop().Value);
                        ops.Push(op);
                        Advance();
                        expectOperand = true;
                    }
                    else break;
                }
            }

            while (ops.Count > 0)
            {
                PolizKind? o = ops.Pop();
                if (o != null) Emit(o.Value);
            }
        }

        private static bool IsBinary(TokenType t)
        {
            return t == TokenType.PLUS || t == TokenType.MINUS || t == TokenType.MULTIPLY;
        }

        private static PolizKind ToKind(TokenType t)
        {
            if (t == TokenType.PLUS) return PolizKind.Add;
            if (t == TokenType.MINUS) return PolizKind.Sub;
            return PolizKind.Mul;
        }

        private static int Priority(PolizKind k)
        {
            if (k == PolizKind.Neg) return 3;
            if (k == PolizKind.Mul) return 2;
            return 1;
        }

        // ---------------------------- семантические проверки ----------------------------

        private bool CheckDeclared(Token id)
        {
            if (declared.Contains(id.Value)) return true;
            Error(id, $"Переменная '{id.Value}' не описана в разделе VAR");
            return false;
        }

        private void CheckNotLoopVar(Token id)
        {
            if (loopVars.Contains(id.Value))
                Error(id, $"Нельзя изменять счётчик цикла FOR '{id.Value}' внутри его тела");
        }

        private void UseVar(Token id)
        {
            if (!CheckDeclared(id)) return;
            if (!assigned.Contains(id.Value) && warned.Add(id.Value))
                Warning(id, $"Переменная '{id.Value}' используется до присваивания значения");
        }

        private void Error(Token t, string msg)
        {
            ErrorCount++;
            Errors.Add(new LexError(Errors.Count + 1, "Семантическая", t.Line, t.Column, msg));
        }

        private void Warning(Token t, string msg)
        {
            Errors.Add(new LexError(Errors.Count + 1, WarningType, t.Line, t.Column, msg));
        }

        // ---------------------------- вспомогательные ----------------------------

        private PolizItem Emit(PolizKind kind, string name = null, int value = 0)
        {
            var item = new PolizItem { Index = Poliz.Count, Kind = kind, Name = name, Value = value };
            Poliz.Add(item);
            return item;
        }

        private Token Cur()
        {
            return pos < tokens.Count ? tokens[pos] : tokens[tokens.Count - 1];
        }

        private void Advance()
        {
            if (pos < tokens.Count - 1) pos++;
        }

        private void Skip(TokenType t)
        {
            if (Cur().Type == t) Advance();
        }
    }

    /// <summary>
    /// Модуль интерпретации ПОЛИЗ.
    /// </summary>
    public class Interpreter
    {
        public Dictionary<string, int> Memory { get; private set; }
        public List<string> Output { get; private set; }
        public string RuntimeError { get; private set; }

        public Interpreter()
        {
            Memory = new Dictionary<string, int>();
            Output = new List<string>();
        }

        public bool Run(List<PolizItem> code, Func<string, int?> input)
        {
            var stack = new Stack<object>();
            int ip = 0;
            long steps = 0;

            try
            {
                checked
                {
                    while (ip < code.Count)
                    {
                        if (++steps > 5000000)
                            throw new InvalidOperationException("Превышен лимит шагов выполнения (возможно, бесконечный цикл)");

                        PolizItem it = code[ip++];
                        switch (it.Kind)
                        {
                            case PolizKind.PushAddr:
                                stack.Push(it.Name);
                                break;

                            case PolizKind.PushConst:
                                stack.Push(it.Value);
                                break;

                            case PolizKind.PushVar:
                                {
                                    int val;
                                    Memory.TryGetValue(it.Name, out val);
                                    stack.Push(val);
                                    break;
                                }

                            case PolizKind.Add: { int b = (int)stack.Pop(); int a = (int)stack.Pop(); stack.Push(a + b); break; }
                            case PolizKind.Sub: { int b = (int)stack.Pop(); int a = (int)stack.Pop(); stack.Push(a - b); break; }
                            case PolizKind.Mul: { int b = (int)stack.Pop(); int a = (int)stack.Pop(); stack.Push(a * b); break; }
                            case PolizKind.Neg: { int a = (int)stack.Pop(); stack.Push(-a); break; }
                            case PolizKind.LessEq: { int b = (int)stack.Pop(); int a = (int)stack.Pop(); stack.Push(a <= b ? 1 : 0); break; }

                            case PolizKind.Assign:
                                {
                                    int v = (int)stack.Pop();
                                    string addr = (string)stack.Pop();
                                    Memory[addr] = v;
                                    break;
                                }

                            case PolizKind.Read:
                                {
                                    string addr = (string)stack.Pop();
                                    int? v = input("Введите значение переменной " + addr + ":");
                                    if (v == null) throw new InvalidOperationException("Ввод отменён пользователем");
                                    Memory[addr] = v.Value;
                                    break;
                                }

                            case PolizKind.Write:
                                Output.Add(((int)stack.Pop()).ToString());
                                break;

                            case PolizKind.Jmp:
                                ip = it.Target;
                                break;

                            case PolizKind.Jz:
                                if ((int)stack.Pop() == 0) ip = it.Target;
                                break;
                        }
                    }
                }
                return true;
            }
            catch (OverflowException)
            {
                RuntimeError = "Арифметическое переполнение при выполнении";
                return false;
            }
            catch (InvalidOperationException ex)
            {
                RuntimeError = ex.Message;
                return false;
            }
        }
    }
}