# Copilot PicoGK — moteur (étape 1)

Programme console : cahier des charges en texte → code C# PicoGK généré par Claude → compilation → exécution → `result.stl`.
Si la compilation ou l'exécution échoue, les erreurs sont renvoyées à Claude, qui corrige (4 tentatives par défaut).

## Prérequis

- SDK .NET 9 (déjà installé chez toi) et le projet `PicoGK_Examples-main` dans `Documents`.
- Une clé API Anthropic, créée sur console.anthropic.com. Elle est distincte de l'abonnement claude.ai et facturée à l'usage.

## Première utilisation

Dans un terminal PowerShell :

```powershell
setx ANTHROPIC_API_KEY "sk-ant-..."
```

Ferme puis rouvre le terminal (la variable n'est prise en compte que dans un nouveau terminal), puis :

```powershell
cd CopilotPicoGK.Engine
dotnet run -- --spec "Une sphère de 40 mm de diamètre percée d'un trou traversant de 10 mm de diamètre"
```

Le viewer PicoGK s'ouvre avec la pièce. Ferme sa fenêtre : le moteur vérifie alors que `result.stl` existe.

## Où trouver les résultats

`Documents\CopilotPicoGK\runs\<date-heure>\` contient `attempt-N.cs` (code de chaque tentative), `result.stl` et `PicoGK.log`.

## Comment ça marche

- `Prompts\rules.md` : règles données à Claude. Tu peux le modifier sans recompiler.
- Les fichiers `.cs` de `PicoGK_Examples-main` sont injectés dans le prompt : ce sont eux qui disent à Claude quelle API PicoGK existe vraiment.
- Le projet `Runner` (créé dans `Documents\CopilotPicoGK\Runner`) reprend automatiquement la version de PicoGK du projet d'exemples.

## Options

`--spec-file cahier.txt`, `--examples <dossier>`, `--workspace <dossier>`, `--attempts <n>`, `--model <nom>` (ou variable `COPILOT_MODEL`).
