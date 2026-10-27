using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace tyap_2
{
    public partial class MainForm : Form
    {
        private DataGridView polizTable;
        private TextBox outputBox;
        private TabPage tabPoliz, tabOutput, tabPda;
        private ToolStripButton btnRun;

        public MainForm()
        {
            InitializeComponent();
            BuildTranslatorUi();
            LoadSampleProgram();
        }

        private void BuildTranslatorUi()
        {
            polizTable = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            polizTable.Columns.Add("pNum", "№");
            polizTable.Columns.Add("pItem", "Элемент ПОЛИЗ");
            polizTable.Columns.Add("pDesc", "Назначение");
            polizTable.Columns[0].FillWeight = 20;

            tabPoliz = new TabPage("ПОЛИЗ") { UseVisualStyleBackColor = true };
            tabPoliz.Controls.Add(polizTable);

            outputBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 11F)
            };
            tabOutput = new TabPage("Вывод") { UseVisualStyleBackColor = true };
            tabOutput.Controls.Add(outputBox);

            TextBox pdaBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 10F),
                Text = Parser.DescribeAutomaton()
            };
            tabPda = new TabPage("МП-автомат") { UseVisualStyleBackColor = true };
            tabPda.Controls.Add(pdaBox);

            resultTabs.TabPages.Add(tabPoliz);
            resultTabs.TabPages.Add(tabOutput);
            resultTabs.TabPages.Add(tabPda);

            // протокол работы автомата: добавляем колонку со стеком
            colSynNonTerm.HeaderText = "Верх стека";
            colSynProduction.HeaderText = "Действие автомата";
            colSynToken.HeaderText = "Вход";
            syntaxTable.Columns.Add("colSynStack", "Стек (вершина слева)");
            syntaxTable.Columns[6].FillWeight = 200;

            btnRun = new ToolStripButton("Выполнить") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            btnRun.Click += (s, e) => RunFullAnalysis(true);
            toolStrip.Items.Insert(toolStrip.Items.IndexOf(btnFullAnalysis) + 1, btnRun);
        }

        private void LoadSampleProgram()
        {
            codeBox.Text =
                "VAR A, B, I : INTEGER;\r\n" +
                "BEGIN\r\n" +
                "    READ(A);\r\n" +
                "    B = 0;\r\n" +
                "    FOR I = 1 TO A DO\r\n" +
                "        B = B + I;\r\n" +
                "    END_FOR;\r\n" +
                "    WRITE(B);\r\n" +
                "END";
        }

        // КНОПКИ

        private void btnClear_Click(object sender, EventArgs e) => ClearAll();
        private void btnSample_Click(object sender, EventArgs e) => LoadSampleProgram();
        private void btnLexAnalysis_Click(object sender, EventArgs e) => RunLexicalAnalysis();
        private void btnSyntaxAnalysis_Click(object sender, EventArgs e) { Lexer l; RunSyntaxAnalysis(out l); }
        private void btnFullAnalysis_Click(object sender, EventArgs e) => RunFullAnalysis(false);

        private void btnAbout_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Транслятор языка (вариант №5)\n\n" +
                "Тип переменных: INTEGER\n" +
                "Унарная операция: -\n" +
                "Бинарные операции: + - *\n" +
                "Константа: последовательность цифр\n" +
                "Макс. длина идентификатора: 10\n" +
                "Распознаватель: нисходящий магазинный автомат, LL(1)\n" +
                "Операторы: READ (1), FOR (2), WRITE (7)\n" +
                "Форма транслятора: интерпретатор (ПОЛИЗ)\n\n" +
                "ГРАММАТИКА (LL(1)):\n\n" +
                "I  -> A B\n" +
                "A  -> VAR D : INTEGER ;\n" +
                "B  -> BEGIN C END\n" +
                "C  -> S C'\n" +
                "C' -> C | ε\n" +
                "S  -> F | READ ( D ) ; | WRITE ( D ) ; | FOR E = G TO G DO C END_FOR ;\n" +
                "F  -> E = G ;\n" +
                "D  -> E D'\n" +
                "D' -> , D | ε\n" +
                "G  -> H J | J\n" +
                "H  -> -\n" +
                "J  -> P J'\n" +
                "J' -> L P J' | ε\n" +
                "P  -> ( G ) | K\n" +
                "K  -> E | M\n" +
                "L  -> + | - | *\n",
                "О программе",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        // СОБЫТИЯ

        private void codeBox_SelectionChanged(object sender, EventArgs e)
        {
            int line = codeBox.GetLineFromCharIndex(codeBox.SelectionStart) + 1;
            int col = codeBox.SelectionStart - codeBox.GetFirstCharIndexFromLine(line - 1) + 1;
            statusPosition.Text = $"Строка: {line}, Столбец: {col}";
        }

        private void errTable_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                string lineStr = errTable.Rows[e.RowIndex].Cells["colErrLine"].Value?.ToString();
                if (int.TryParse(lineStr, out int line))
                {
                    int index = codeBox.GetFirstCharIndexFromLine(line - 1);
                    if (index >= 0)
                    {
                        codeBox.SelectionStart = index;
                        codeBox.SelectionLength = 0;
                        codeBox.Focus();
                    }
                }
            }
        }

        // ВСПОМОГАТЕЛЬНЫЕ

        private void ClearAll()
        {
            codeBox.Clear();
            ClearResults();
            statusLabel.Text = "Готово";
            statusLexCount.Text = "Лексем: 0";
            statusErrCount.Text = "Ошибок: 0";
        }

        private void ClearResults()
        {
            lexTable.Rows.Clear();
            symTable.Rows.Clear();
            syntaxTable.Rows.Clear();
            errTable.Rows.Clear();
            polizTable.Rows.Clear();
            outputBox.Clear();
        }

        private void FillLexTables(Lexer lexer)
        {
            int n = 1;
            foreach (Token token in lexer.Tokens)
            {
                if (token.Type == TokenType.EOF)
                    lexTable.Rows.Add(n++, "EOF", token.Code, "", token.Line, token.Column);
                else
                    lexTable.Rows.Add(n++, Lexer.GetRussianName(token.Type),
                        token.Code, token.Value, token.Line, token.Column);
            }
            foreach (Symbol sym in lexer.SymbolTable.Symbols)
                symTable.Rows.Add(sym.Index, sym.Name, sym.Type, sym.Value, sym.Address, sym.Line);

            statusLexCount.Text = $"Лексем: {lexer.Tokens.Count}";
        }

        private void ShowErrors(List<LexError> errors, int errorCount)
        {
            foreach (LexError err in errors)
                errTable.Rows.Add(err.Index, err.Type, err.Line, err.Column, err.Message);
            statusErrCount.Text = $"Ошибок: {errorCount}";
            resultTabs.SelectedTab = tabErrors;
        }

        // АНАЛИЗ

        private void RunLexicalAnalysis()
        {
            statusLabel.Text = "Лексический анализ...";
            Application.DoEvents();

            ClearResults();

            Lexer lexer = new Lexer(codeBox.Text);
            lexer.Analyze();

            FillLexTables(lexer);

            foreach (LexError err in lexer.Errors)
                errTable.Rows.Add(err.Index, err.Type, err.Line, err.Column, err.Message);

            statusErrCount.Text = $"Ошибок: {lexer.Errors.Count}";

            if (lexer.Errors.Count > 0)
                statusLabel.Text = $"Лексический анализ: {lexer.Errors.Count} ошибок";
            else
                statusLabel.Text = "Лексический анализ завершён успешно";

            resultTabs.SelectedTab = tabLexems;
        }

        private bool RunSyntaxAnalysis(out Lexer lexer)
        {
            statusLabel.Text = "Синтаксический анализ...";
            Application.DoEvents();
            ClearResults();

            lexer = new Lexer(codeBox.Text);
            lexer.Analyze();
            FillLexTables(lexer);

            if (lexer.Errors.Count > 0)
            {
                ShowErrors(lexer.Errors, lexer.Errors.Count);
                statusLabel.Text = "Синтаксический анализ прерван: есть лексические ошибки";
                return false;
            }

            Parser parser = new Parser(lexer.Tokens);
            parser.Parse();

            foreach (ParseRule rule in parser.Rules)
                syntaxTable.Rows.Add(rule.Step, rule.NonTerminal, rule.Production,
                                     rule.Token, rule.Line, rule.Column, rule.Stack);

            if (parser.Errors.Count > 0)
            {
                ShowErrors(parser.Errors, parser.Errors.Count);
                statusLabel.Text = $"Синтаксический анализ: {parser.Errors.Count} ошибок";
                return false;
            }

            statusErrCount.Text = "Ошибок: 0";
            statusLabel.Text = "Синтаксический анализ завершён успешно, программа синтаксически верна";
            resultTabs.SelectedTab = tabSyntax;
            return true;
        }

        private void RunFullAnalysis(bool execute)
        {
            Lexer lexer;
            if (!RunSyntaxAnalysis(out lexer)) return;

            statusLabel.Text = "Семантический анализ...";
            Application.DoEvents();

            Translator tr = new Translator(lexer.Tokens);
            tr.Translate();

            foreach (PolizItem it in tr.Poliz)
                polizTable.Rows.Add(it.Index, it.Text, it.Description);

            if (tr.Errors.Count > 0)
                ShowErrors(tr.Errors, tr.ErrorCount);

            if (tr.ErrorCount > 0)
            {
                statusLabel.Text = $"Семантический анализ: {tr.ErrorCount} ошибок";
                return;
            }

            statusLabel.Text = "Семантический анализ завершён успешно, ПОЛИЗ построен";
            if (tr.Errors.Count == 0) resultTabs.SelectedTab = tabPoliz;

            if (!execute) return;

            Interpreter interp = new Interpreter();
            bool ok = interp.Run(tr.Poliz, AskInteger);

            outputBox.Text = string.Join(Environment.NewLine, interp.Output);

            foreach (DataGridViewRow row in symTable.Rows)
            {
                int v;
                string name = row.Cells[1].Value?.ToString() ?? "";
                if (interp.Memory.TryGetValue(name, out v))
                    row.Cells[3].Value = v;
            }

            if (!ok)
            {
                errTable.Rows.Add(errTable.Rows.Count + 1, "Выполнение", "", "", interp.RuntimeError);
                statusLabel.Text = "Ошибка выполнения: " + interp.RuntimeError;
                resultTabs.SelectedTab = tabErrors;
            }
            else
            {
                statusLabel.Text = "Программа выполнена успешно";
                resultTabs.SelectedTab = tabOutput;
            }
        }

        private static int? AskInteger(string prompt)
        {
            using (Form f = new Form())
            {
                f.Text = "Ввод данных";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ClientSize = new Size(300, 110);

                Label lbl = new Label { Text = prompt, Left = 12, Top = 12, Width = 276 };
                TextBox tb = new TextBox { Left = 12, Top = 38, Width = 276 };
                Button ok = new Button { Text = "OK", Left = 132, Top = 70, Width = 75, DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = "Отмена", Left = 213, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                while (f.ShowDialog() == DialogResult.OK)
                {
                    int v;
                    if (int.TryParse(tb.Text.Trim(), out v)) return v;
                    MessageBox.Show("Введите целое число");
                }
                return null;
            }
        }
    }
}