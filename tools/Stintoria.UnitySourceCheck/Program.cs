using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = args.Length == 1 ? args[0] : "integrations/unity-client/Assets/Stintoria";
var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(x => x).ToArray();
if (files.Length != 8) throw new Exception("Esperadas oito fontes Unity (incluindo cena, corrida e armazenamento Web); conferir pacote.");
var failures = 0;
foreach (var file in files) {
    var text = File.ReadAllText(file);
    var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp8), file);
    foreach (var symbols in new[] { Array.Empty<string>(), new[] { "UNITY_WEBGL" } })
        foreach (var diagnostic in CSharpSyntaxTree.ParseText(text,
            new CSharpParseOptions(LanguageVersion.CSharp8).WithPreprocessorSymbols(symbols), file)
            .GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)) {
            Console.WriteLine(diagnostic); failures++;
        }
    foreach (var allocation in tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>()) {
        var type = allocation.Type.ToString();
        if (type.EndsWith("RaceSimulation") || type.EndsWith("Random") || type.EndsWith("CareerSession"))
        { Console.WriteLine("ERRO: fonte UI tenta criar simulação/agregado: " + file); failures++; }
    }
    if (text.Contains("UnityWebRequest") || text.Contains("System.Net") || text.Contains("Time.deltaTime"))
    { Console.WriteLine("ERRO: dependência de rede/tempo não autorizada: " + file); failures++; }
    Console.WriteLine("SINTAXE C# 8 analisada: " + file);
}
var scenePath = Path.Combine(root, "Runtime", "StintoriaKartScene.cs");
if (!File.Exists(scenePath)) {
    Console.WriteLine("ERRO: falta a cena visual Kart de fonte.");
    failures++;
} else {
    var scene = File.ReadAllText(scenePath);
    foreach (var contract in new[] {"PrimitiveType.Plane", "PrimitiveType.Cube", "PrimitiveType.Cylinder",
        "PrimitiveType.Sphere", "Camera", "ClientRaceSession", "RaceState",
        "drivers.Length", "state.Classification", "row.ElapsedMs", "protagonistMarker"})
        if (!scene.Contains(contract)) { Console.WriteLine("ERRO: cena sem contrato visual " + contract); failures++; }
    if (scene.Contains("Time.deltaTime") || scene.Contains("UnityEngine.Random")) {
        Console.WriteLine("ERRO: cena não pode avançar ou sortear dados simulados.");
        failures++;
    }
}
var appPath = Path.Combine(root, "Runtime", "StintoriaApp.cs");
if (!File.Exists(appPath)) {
    Console.WriteLine("ERRO: falta a interface ligada ao controlador real.");
    failures++;
} else {
    var appFiles = Directory.GetFiles(Path.Combine(root, "Runtime"), "StintoriaApp*.cs");
    var app = string.Join("\n", appFiles.Select(File.ReadAllText));
    foreach (var connection in new[] {"client.DriverRoster()", "client.ActiveRace()",
        "kartScene.ToggleCamera()", "kartScene.SetEngineSnapshot", "client.StartRaceAsync",
        "client.AdvanceRaceAsync", "client.SaveAsync"})
        if (!app.Contains(connection)) { Console.WriteLine("ERRO: interface sem ligação real " + connection); failures++; }
}
Console.WriteLine("Somente sintaxe e restrições estáticas. NÃO valida símbolos/APIs Unity, importação, renderização ou build.");
return failures == 0 ? 0 : 1;
