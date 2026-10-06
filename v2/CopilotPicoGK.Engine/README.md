# Copilot PicoGK — moteur (étape 1, IA locale hors ligne)

Programme console : cahier des charges en texte → code C# PicoGK généré par un **modèle local** (Ollama) → compilation → exécution → `result.stl`.
Si la compilation ou l'exécution échoue, l'erreur est renvoyée au modèle, qui corrige (4 tentatives par défaut).

Aucune clé API, aucun service en ligne : l'IA est un composant à part (Ollama) qui tourne sur ce PC.

## Prérequis

- SDK .NET 9 et le projet `PicoGK_Examples-main` dans `Documents` (déjà en place).
- **Ollama** pour Windows (ollama.com). Il tourne en service d'arrière-plan et écoute sur `localhost:11434`.
- Un modèle de code, à télécharger une seule fois (la suite fonctionne sans Internet) :

| Mémoire vidéo (GPU) | Commande |
|---|---|
| 8 Go | `ollama pull qwen2.5-coder:7b` |
| 16 Go | `ollama pull qwen2.5-coder:14b` |
| 24 Go ou plus | `ollama pull qwen3-coder:30b` |

Sans GPU dédié, ça fonctionne aussi, mais beaucoup plus lentement : prends le 7b.

## Première utilisation

```powershell
cd CopilotPicoGK.Engine
dotnet run -- --spec "Une sphère de 40 mm de diamètre percée d'un trou traversant de 10 mm de diamètre" --model qwen2.5-coder:7b
```

Le viewer PicoGK s'ouvre avec la pièce. Ferme sa fenêtre : le moteur vérifie alors que `result.stl` existe.
Pour ne plus retaper le modèle : `setx COPILOT_MODEL "qwen2.5-coder:14b"` (puis nouveau terminal).

## Hors ligne : ce qui doit être fait une fois avec Internet

- Télécharger le modèle Ollama (`ollama pull ...`).
- Avoir restauré le paquet NuGet PicoGK : c'est déjà fait si `PicoGK_Examples` se lance chez toi. Le Runner utilise la même version, déjà présente dans le cache NuGet.

## Résultats

`Documents\CopilotPicoGK\runs\<date-heure>\` : `attempt-N.cs` (code de chaque tentative), `result.stl`, `PicoGK.log`.

## Réglages utiles

- `--context 16384` : fenêtre de contexte demandée à Ollama. Elle détermine combien d'exemples PicoGK entrent dans le prompt. Si Ollama est lent ou sature la mémoire vidéo, descends à 12288 (minimum).
- `Prompts\rules.md` : règles données au modèle, modifiables sans recompiler.
- Les `.cs` de `PicoGK_Examples-main` sont injectés dans le prompt : ils indiquent au modèle quelle API PicoGK existe vraiment.

## Architecture

`ILlmClient` est la seule chose que le moteur connaît de l'IA. `OllamaClient` l'implémente pour Ollama ; un autre moteur local (llama.cpp, LM Studio) s'ajoute avec une classe de plus, sans toucher au reste.
