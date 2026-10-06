# Copilot PicoGK — moteur (étape 1)

Programme console : cahier des charges en texte → code C# PicoGK généré par une IA → compilation → exécution → `result.stl`.
Si la compilation ou l'exécution échoue, l'erreur est renvoyée à l'IA, qui corrige (4 tentatives par défaut).

L'IA est un composant séparé, derrière l'interface `ILlmClient` :
- **OpenRouter** (défaut) : modèle `qwen/qwen3-coder:free`.
- **Ollama** (optionnel, local) : `--backend ollama`.

## Prérequis

- SDK .NET 9 et le projet `PicoGK_Examples-main` dans `Documents` (déjà en place).
- Un compte OpenRouter et une clé API (openrouter.ai).
- Dans les paramètres de confidentialité de ton compte OpenRouter, autoriser l'usage des modèles gratuits (sinon le modèle `:free` est refusé).

## Clé API : jamais dans un fichier

Dans PowerShell, avec ta propre clé :

```powershell
setx OPENROUTER_API_KEY "sk-or-..."
```

Ferme puis rouvre le terminal (la variable n'est lue que par les nouveaux terminaux).
Ne mets jamais la clé dans le code, dans un zip ou dans un dépôt Git.

## Utilisation

```powershell
cd CopilotPicoGK.Engine
dotnet run -- --spec "Une sphère de 40 mm de diamètre percée d'un trou traversant de 10 mm de diamètre"
```

Le viewer PicoGK s'ouvre avec la pièce. Ferme sa fenêtre : le moteur vérifie alors que `result.stl` existe.

## Limites du palier gratuit OpenRouter

- Environ 20 requêtes par minute, et 50 requêtes par jour (1000 par jour après un achat unique de 10 $ de crédit).
- Une génération = 1 requête, plus 1 par correction : compte jusqu'à 4 requêtes par cahier des charges.
- Quota atteint : le moteur l'indique clairement. Les conditions et le catalogue des modèles gratuits changent souvent : vérifie sur openrouter.ai.

## Résultats

`Documents\CopilotPicoGK\runs\<date-heure>\` : `attempt-N.cs` (code de chaque tentative), `result.stl`, `PicoGK.log`.

## Réglages utiles

- `--model <id>` ou variable `COPILOT_MODEL` : tout modèle OpenRouter (par exemple la version payante `qwen/qwen3-coder`).
- `--context` : règle le nombre d'exemples PicoGK injectés dans le prompt.
- `Prompts\rules.md` : règles données au modèle, modifiables sans recompiler.
- Les `.cs` de `PicoGK_Examples-main` sont injectés dans le prompt : ils indiquent à l'IA quelle API PicoGK existe vraiment.

## Mode local (optionnel)

Installer Ollama, `ollama pull qwen2.5-coder:7b` (ou 14b / `qwen3-coder:30b` selon le GPU), puis `dotnet run -- --backend ollama --spec "..."`.
