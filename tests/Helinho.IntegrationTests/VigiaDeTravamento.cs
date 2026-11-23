// DIAGNÓSTICO TEMPORÁRIO (#120): remover antes do merge.
// O `validate` trava no passo `dotnet test` só no runner do GitHub. Este vigia registra os testes em execução e,
// passado o limite, escreve o que está preso (testes, pool de threads, pilhas das threads em espera, chamadas de
// sistema das threads no Linux) e encerra o processo com código 97: a run falha com log legível em vez de travar.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Xunit.v3;

[assembly: Helinho.IntegrationTests.VigiaDeTravamento]

namespace Helinho.IntegrationTests;

// DIAGNÓSTICO TEMPORÁRIO (#120): remover antes do merge.
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class VigiaDeTravamentoAttribute : BeforeAfterTestAttribute
{
    /// <summary>Limite em segundos; sem ela, o vigia só liga no GitHub Actions (12 min).</summary>
    internal const string VariavelDoLimite = "HELINHO_VIGIA_SEGUNDOS";
    private const int CodigoDeSaida = 97;

    private sealed record Teste(string Nome, long InicioMs);

    private static readonly Stopwatch Relogio = Stopwatch.StartNew();
    private static readonly ConcurrentDictionary<string, Teste> EmExecucao = new();
    private static readonly ConcurrentDictionary<Thread, Teste> ThreadsDosTestes = new();
    private static readonly ConcurrentQueue<string> Terminados = new();
    private static readonly ConcurrentDictionary<int, byte> Interrompidas = new();
    // O manipulador roda em toda troca de contexto de execução: sabe em que threads o fluxo de cada teste está agora.
    private static readonly AsyncLocal<Teste?> Atual = new(troca =>
    {
        if (troca.CurrentValue is { } teste)
        {
            ThreadsDosTestes[Thread.CurrentThread] = teste;
        }
        else
        {
            ThreadsDosTestes.TryRemove(Thread.CurrentThread, out _);
        }
    });

    private static readonly string[] TabelasDeSoquetes = ["/proc/net/tcp", "/proc/net/tcp6"];
    private static int _iniciados;
    private static int _terminados;
    private static string? _fluxoBloqueado;

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var teste = new Teste(test.TestDisplayName, Relogio.ElapsedMilliseconds);
        EmExecucao[test.UniqueID] = teste;
        Interlocked.Increment(ref _iniciados);
        Atual.Value = teste;
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        Atual.Value = null;
        if (EmExecucao.TryRemove(test.UniqueID, out var teste))
        {
            Interlocked.Increment(ref _terminados);
            Terminados.Enqueue($"{teste.Nome} ({Relogio.ElapsedMilliseconds - teste.InicioMs} ms)");
            while (Terminados.Count > 12 && Terminados.TryDequeue(out _))
            {
            }
        }
    }

    [ModuleInitializer]
    internal static void Ligar()
    {
        var limite = int.TryParse(Environment.GetEnvironmentVariable(VariavelDoLimite), CultureInfo.InvariantCulture, out var segundos)
            ? segundos
            : Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? 720 : 0;
        if (limite > 0)
        {
            // Thread própria (não o pool nem um Timer): tem de disparar mesmo com o pool de threads esgotado.
            new Thread(() => Vigiar(TimeSpan.FromSeconds(limite))) { IsBackground = true, Name = "vigia-120" }.Start();
        }
    }

    private static void Vigiar(TimeSpan limite)
    {
        var batida = TimeSpan.FromSeconds(Math.Max(5, limite.TotalSeconds / 6));
        while (Relogio.Elapsed + batida < limite)
        {
            Thread.Sleep(batida);
            Escrever($"[vigia-120] {Relogio.Elapsed:mm\\:ss} iniciados={_iniciados} terminados={_terminados} em execução={EmExecucao.Count} {Pool()}");
        }

        Thread.Sleep(limite > Relogio.Elapsed ? limite - Relogio.Elapsed : TimeSpan.Zero);
        Escrever(Relatorio(limite));

        // Interromper uma thread em espera gerenciada lança ThreadInterruptedException no ponto da espera;
        // o manipulador de primeira chance roda nessa thread, antes do desempilhamento, e imprime a pilha inteira.
        AppDomain.CurrentDomain.FirstChanceException += AoInterromper;
        foreach (var thread in ThreadsDosTestes.Keys.Where(t => t.ThreadState.HasFlag(System.Threading.ThreadState.WaitSleepJoin)))
        {
            thread.Interrupt();
        }

        Thread.Sleep(TimeSpan.FromSeconds(8));
        Escrever($"[vigia-120] encerrando o processo com o código {CodigoDeSaida}.");
        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(20)); // se o Exit ficar preso em algum manipulador de saída
            Process.GetCurrentProcess().Kill();
        })
        { IsBackground = true }.Start();
        Environment.Exit(CodigoDeSaida);
    }

    private static void AoInterromper(object? sender, FirstChanceExceptionEventArgs e)
    {
        // Só a primeira vez por thread (os relançamentos repetem o evento com a pilha já desfeita).
        if (e.Exception is ThreadInterruptedException && Interrompidas.TryAdd(Environment.CurrentManagedThreadId, 0))
        {
            ThreadsDosTestes.TryGetValue(Thread.CurrentThread, out var teste);
            Escrever($"[vigia-120] PILHA da thread {Environment.CurrentManagedThreadId} (teste: {teste?.Nome ?? "?"}):{Environment.NewLine}{new StackTrace(1, false)}");
            // Segura a exceção até o vigia encerrar o processo: sem tratamento, ela o derrubaria antes de as outras pilhas saírem.
            Thread.Sleep(Timeout.Infinite);
        }
    }

    private static string Relatorio(TimeSpan limite)
    {
        var agora = Relogio.ElapsedMilliseconds;
        var texto = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"[vigia-120] ===== LIMITE DE {limite.TotalSeconds:0} s ATINGIDO: a suíte não terminou =====")
            .AppendLine(CultureInfo.InvariantCulture, $"[vigia-120] iniciados={_iniciados} terminados={_terminados} núcleos={Environment.ProcessorCount} threads do processo={Process.GetCurrentProcess().Threads.Count}")
            .AppendLine(CultureInfo.InvariantCulture, $"[vigia-120] {Pool()} {Sonda()}")
            .AppendLine("[vigia-120] TESTES EM EXECUÇÃO:");
        foreach (var teste in EmExecucao.Values.OrderBy(t => t.InicioMs))
        {
            texto.AppendLine(CultureInfo.InvariantCulture, $"[vigia-120]   há {(agora - teste.InicioMs) / 1000} s: {teste.Nome}");
        }

        texto.AppendLine("[vigia-120] THREADS NO FLUXO DE ALGUM TESTE:");
        foreach (var (thread, teste) in ThreadsDosTestes)
        {
            texto.AppendLine(CultureInfo.InvariantCulture, $"[vigia-120]   thread {thread.ManagedThreadId} pool={thread.IsThreadPoolThread} estado={thread.ThreadState}: {teste.Nome}");
        }

        texto.AppendLine("[vigia-120] ÚLTIMOS TESTES TERMINADOS:");
        foreach (var nome in Terminados)
        {
            texto.AppendLine(CultureInfo.InvariantCulture, $"[vigia-120]   {nome}");
        }

        return texto.Append(ThreadsDoNucleo()).ToString();
    }

    private static string Pool()
    {
        ThreadPool.GetAvailableThreads(out var livres, out _);
        ThreadPool.GetMinThreads(out var minimo, out _);
        return $"pool: threads={ThreadPool.ThreadCount} mínimo={minimo} livres={livres} pendentes={ThreadPool.PendingWorkItemCount} concluídos={ThreadPool.CompletedWorkItemCount}";
    }

    /// <summary>Um item de trabalho novo é atendido pelo pool em até 3 s? Se não, o pool está esgotado.</summary>
    private static string Sonda()
    {
        using var atendido = new ManualResetEventSlim();
        var inicio = Relogio.ElapsedMilliseconds;
        // O item pode rodar depois do descarte do evento, quando o pool está esgotado: por isso o try.
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            try { atendido.Set(); }
            catch (ObjectDisposedException) { }
        }, null);
        return atendido.Wait(TimeSpan.FromSeconds(3))
            ? $"sonda do pool: atendida em {Relogio.ElapsedMilliseconds - inicio} ms"
            : "sonda do pool: NÃO atendida em 3 s (pool esgotado)";
    }

    /// <summary>Linux: em que chamada de sistema cada thread do processo está, com o destino do descritor quando houver.</summary>
    private static string ThreadsDoNucleo()
    {
        if (!OperatingSystem.IsLinux())
        {
            return string.Empty;
        }

        var texto = new StringBuilder("[vigia-120] THREADS DO PROCESSO POR CHAMADA DE SISTEMA (x64: 0 read, 1 write, 7 poll, 42 connect, 44 sendto, 45 recvfrom, 46 sendmsg, 47 recvmsg, 202 futex, 232 epoll_wait, 271 ppoll):").AppendLine();
        try
        {
            var soquetes = TabelasDeSoquetes.Where(File.Exists).SelectMany(a => File.ReadLines(a).Skip(1))
                .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(c => c.Length > 9)
                .GroupBy(c => c[9]).ToDictionary(g => g.Key, g => $"{g.First()[1]}->{g.First()[2]} estado={g.First()[3]}");
            var grupos = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var pasta in Directory.GetDirectories("/proc/self/task"))
            {
                var chamada = File.ReadAllText(Path.Combine(pasta, "syscall")).Trim().Split(' ');
                var descricao = chamada[0];
                if (chamada.Length > 1 && chamada[0] is "0" or "1" or "42" or "44" or "45" or "46" or "47")
                {
                    var descritor = Convert.ToInt64(chamada[1], 16);
                    var alvo = new FileInfo($"/proc/self/fd/{descritor}").LinkTarget ?? "?";
                    descricao += $" fd={descritor} {alvo} {(alvo.StartsWith("socket:[", StringComparison.Ordinal) ? soquetes.GetValueOrDefault(alvo[8..^1]) : null)}";
                }

                var chave = $"{File.ReadAllText(Path.Combine(pasta, "comm")).Trim()} | chamada {descricao}";
                grupos[chave] = grupos.GetValueOrDefault(chave) + 1;
            }

            foreach (var (chave, quantidade) in grupos)
            {
                texto.AppendLine(CultureInfo.InvariantCulture, $"[vigia-120]   {quantidade}x {chave}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            texto.AppendLine(CultureInfo.InvariantCulture, $"[vigia-120]   leitura de /proc falhou: {ex.GetType().Name}");
        }

        return texto.ToString();
    }

    /// <summary>
    /// Escreve direto nos fluxos do processo (o xUnit captura o <c>Console</c> dos testes). Cada escrita roda em thread
    /// própria com espera limitada: se o próprio fluxo estiver bloqueado, o vigia não fica preso junto.
    /// </summary>
    private static void Escrever(string texto)
    {
        var aviso = _fluxoBloqueado is null ? null : $"[vigia-120] ATENÇÃO: uma escrita anterior em {_fluxoBloqueado} não terminou em 10 s.{Environment.NewLine}";
        var bytes = Encoding.UTF8.GetBytes(aviso + texto + Environment.NewLine);
        foreach (var (nome, abrir) in new (string, Func<Stream>)[] { ("stderr", Console.OpenStandardError), ("stdout", Console.OpenStandardOutput) })
        {
            var escrita = new Thread(() =>
            {
                using var fluxo = abrir();
                fluxo.Write(bytes);
                fluxo.Flush();
            })
            { IsBackground = true };
            escrita.Start();
            if (!escrita.Join(TimeSpan.FromSeconds(10)))
            {
                _fluxoBloqueado = nome;
            }
        }
    }
}
