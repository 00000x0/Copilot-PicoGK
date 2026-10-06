# Copilot PicoGK — application de bureau

Décris une pièce en français : l'IA de ton choix écrit le code PicoGK, le moteur le compile, l'exécute et ouvre la fenêtre 3D. Tu peux ensuite demander une modification de cette pièce, autant de fois que tu veux.

## Contenu

- `copilot_picogk_app.py` : l'application (Python, aucune bibliothèque à installer).
- `Lancer_Copilot_PicoGK.bat` : double-clic pour la lancer.
- `CopilotPicoGK.Engine/` : le moteur C# (compile et lance le code généré). Garde ce dossier à côté de l'application.

## Prérequis

- Python 3.10 ou plus récent : `winget install Python.Python.3.12`.
- SDK .NET 9 et `PicoGK_Examples-main` dans Documents (déjà en place chez toi).
- Une IA, au choix : une clé OpenRouter, Ollama installé sur le PC, ou une clé d'un autre fournisseur compatible OpenAI.

## Premier lancement

1. Double-clique sur `Lancer_Copilot_PicoGK.bat`. Si la fenêtre ne s'ouvre pas, ouvre un terminal dans ce dossier et lance `py copilot_picogk_app.py` pour voir le message d'erreur.
2. Les trois pastilles en haut à droite doivent être vertes (SDK .NET, moteur, exemples PicoGK). Sinon, onglet « Paramètres ».
3. Onglet « IA & quotas » : vérifie ton IA (bouton « Vérifier… »). Pour OpenRouter, ta clé peut venir de la variable d'environnement `OPENROUTER_API_KEY` (déjà définie chez toi : laisse le champ vide).
4. Onglet « Créer » : choisis l'IA, décris la pièce, clique sur « Envoyer ». Un terminal s'ouvre avec l'avancement et les erreurs, puis la fenêtre 3D PicoGK.
5. Pour changer la pièce : « Modifier la pièce actuelle », décris le changement, envoie. Pas besoin de fermer la fenêtre 3D précédente.

## Clés API

Elles ne sont écrites nulle part par l'application. Elles sont transmises au moteur par variable d'environnement, jamais dans la ligne de commande. Ne les mets jamais dans un fichier ou un zip.

## Fichiers produits

`Documents\CopilotPicoGK\runs\<date-heure>\` : `run.json` (état, tokens, modèle), `attempt-N.cs` (code de chaque tentative), `result.stl`, `PicoGK.log`.
