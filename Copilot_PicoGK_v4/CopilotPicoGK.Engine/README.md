# Copilot PicoGK — moteur

Programme console (C#, .NET 9) appelé par l'application de bureau. Il peut aussi se lancer seul :

```powershell
dotnet run -- --spec "Une sphère de 40 mm percée d'un trou de 10 mm"
```

Principe : demande en texte → code C# PicoGK écrit par une IA → compilation → exécution → `result.stl`. En cas d'erreur de compilation ou d'exécution, l'erreur est renvoyée à l'IA, qui corrige (4 tentatives par défaut).

## Fournisseurs d'IA (`--backend`)

- `openrouter` (défaut) : clé dans la variable `OPENROUTER_API_KEY`. `--model auto` choisit les meilleurs modèles gratuits du moment dans le catalogue en direct ; sinon un modèle, ou une liste séparée par des virgules (essayés dans l'ordre).
- `ollama` : modèle local (`--model qwen2.5-coder:7b`, par exemple).
- `openai` : toute API compatible OpenAI (Groq, Mistral, LM Studio…) : `--base-url` et `--model` obligatoires, clé dans la variable donnée par `--api-key-env` (défaut `COPILOT_API_KEY`).

## Autres options

`--base-code <attempt-N.cs>` modifier une pièce existante · `--run-dir` · `--pause` garder le terminal ouvert · `--examples` · `--workspace` · `--attempts` · `--context` · `--spec-file`.

## Fichiers du run

`run.json` est mis à jour à chaque étape (étape, tentative, modèle utilisé, requêtes, tokens, coût, limites annoncées par le fournisseur). L'application de bureau le lit pour afficher le suivi.

Les clés ne sont jamais passées en argument : seulement par variable d'environnement.
