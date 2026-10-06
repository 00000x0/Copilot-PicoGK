#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Copilot PicoGK — application de bureau.

Tu décris une pièce (ou une idée), l'IA de ton choix écrit le code PicoGK, le moteur
(CopilotPicoGK.Engine, en C#) le compile, l'exécute et ouvre la fenêtre 3D. Tu peux ensuite
demander une modification de cette pièce, autant de fois que tu veux.

Aucune bibliothèque à installer : seulement Python 3.10 ou plus récent (Tkinter est fourni avec).
Les clés API ne sont jamais écrites sur le disque par cette application.
"""
from __future__ import annotations

import json
import os
import queue
import shutil
import subprocess
import sys
import threading
import time
import traceback
import urllib.error
import urllib.request
import webbrowser
from datetime import datetime, timedelta, timezone
from pathlib import Path
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from tkinter.scrolledtext import ScrolledText

APP_NAME = "Copilot PicoGK"
APP_DIR = Path(__file__).resolve().parent
OPENROUTER = "https://openrouter.ai/api/v1"

COLORS = {"ok": "#1a7f37", "warn": "#b35900", "bad": "#cf222e", "dim": "#6e7781", "title": "#0b3d91"}

PROVIDERS = [
    ("openrouter", "OpenRouter — en ligne, modèles gratuits ou payants (clé OpenRouter)"),
    ("ollama", "IA installée sur ce PC — Ollama (hors ligne, gratuit, sans clé)"),
    ("other", "Autre API — Groq, Mistral, LM Studio… (ta propre clé)"),
]
PROVIDER_NAMES = {"openrouter": "OpenRouter", "ollama": "IA locale (Ollama)", "other": "Autre API"}

# nom affiché -> (adresse de l'API, variable d'environnement habituelle de la clé)
PRESETS = {
    "Groq": ("https://api.groq.com/openai/v1", "GROQ_API_KEY"),
    "Mistral": ("https://api.mistral.ai/v1", "MISTRAL_API_KEY"),
    "DeepSeek": ("https://api.deepseek.com/v1", "DEEPSEEK_API_KEY"),
    "Together AI": ("https://api.together.xyz/v1", "TOGETHER_API_KEY"),
    "LM Studio (local)": ("http://localhost:1234/v1", ""),
    "llama.cpp server (local)": ("http://localhost:8080/v1", ""),
    "Personnalisé": ("", ""),
}

STAGES = {
    "init": "Démarrage",
    "restauration": "Préparation du projet PicoGK",
    "génération": "L'IA écrit le code",
    "compilation": "Compilation du code",
    "exécution": "Exécution — fenêtre 3D PicoGK",
    "terminé": "Terminé",
}
STATUS_TEXT = {
    "success": ("✔ Réussi", "ok"),
    "failed": ("✘ Échec", "bad"),
    "error": ("✘ Erreur", "bad"),
    "running": ("… En cours", "warn"),
}

EXAMPLES_NEW = [
    "Une sphère de 40 mm de diamètre percée d'un trou traversant de 10 mm de diamètre",
    "Un cube de 30 mm de côté avec une cavité sphérique de 20 mm de diamètre en son centre",
    "Un tube de 60 mm de long, 20 mm de diamètre extérieur et 2 mm d'épaisseur de paroi",
    "Un support en L : deux plaques de 60 x 40 mm et 5 mm d'épaisseur, avec deux trous de 6 mm",
]
EXAMPLES_MODIFY = [
    "Agrandis le trou central à 14 mm de diamètre",
    "Double l'épaisseur des parois",
    "Ajoute un deuxième trou de 6 mm de diamètre sur le côté",
    "Réduis toute la pièce de 25 %",
]

DEFAULTS = {
    "provider": "openrouter",
    "max_attempts": 4,
    "engine_dir": "", "examples_dir": "", "workspace_dir": "", "dotnet": "dotnet",
    "or_model": "auto",
    "ol_url": "http://localhost:11434", "ol_model": "", "ol_context": 16384,
    "ot_preset": "Groq", "ot_url": PRESETS["Groq"][0], "ot_model": "",
}

HELP_TEXT = """\
COMMENT ÇA MARCHE
1. Tu choisis l'IA qui va écrire le code (étape ①).
2. Tu décris la pièce en français, avec ses dimensions en millimètres (étape ②).
3. Tu cliques sur « Envoyer » : un terminal s'ouvre et montre l'avancement en direct (génération du code, compilation, corrections automatiques, erreurs éventuelles).
4. Quand la pièce est prête, la fenêtre 3D de PicoGK s'ouvre. Pour en changer, choisis « Modifier la pièce actuelle », décris le changement, et envoie. Tu peux le faire sans fermer la fenêtre 3D précédente.

LES TROIS TYPES D'IA
• OpenRouter : une IA en ligne. Avec le mode « auto », le moteur choisit lui-même, dans le catalogue du moment, les meilleurs modèles gratuits pour le code. Il faut une clé OpenRouter et Internet.
• IA installée sur ce PC (Ollama) : tout reste sur ton ordinateur, sans clé ni quota ni Internet. La qualité dépend de ta carte graphique : plus le modèle est gros, meilleur il est, mais plus il demande de mémoire vidéo.
• Autre API : n'importe quel service compatible OpenAI (Groq, Mistral, DeepSeek, Together…) ou un serveur local (LM Studio, llama.cpp). Tu donnes l'adresse, le nom du modèle et ta clé.

REQUÊTES, TOKENS, CRÉDITS : QUE SIGNIFIENT LES CHIFFRES ?
• Requête : un appel à l'IA. Une pièce demande 1 requête si tout marche du premier coup, jusqu'à 4 (ou plus selon le réglage) si l'IA doit corriger ses erreurs.
• Token : un morceau de texte (environ 3 à 4 caractères de code). « Tokens envoyés » = ce que tu envoies à l'IA (règles, exemples PicoGK, ta demande). « Tokens reçus » = le code qu'elle te répond.
• OpenRouter, modèles gratuits (:free) : pas de limite en tokens, mais 20 requêtes par minute et 50 requêtes par jour (1000 par jour si tu as acheté au moins 10 $ de crédits au total). La remise à zéro se fait à minuit UTC.
• OpenRouter, modèles payants : seuls comptent les crédits en dollars, affichés dans l'onglet « IA & quotas ».
• Autres API : les limites dépendent du fournisseur. Quand il les annonce (Groq par exemple), elles s'affichent dans le terminal après chaque requête et dans l'historique.
• IA locale : aucun quota. La seule limite est la taille du contexte réglée dans l'onglet « IA & quotas ».
• Les « restantes » affichées par cette application sont des estimations : elles ne comptent que ce que CE programme a envoyé.

MODIFIER UNE PIÈCE
« Modifier la pièce actuelle » envoie à l'IA le code de la pièce déjà réussie, avec ta demande de changement. L'IA renvoie le code complet modifié. Dans l'onglet « Historique », tu peux choisir n'importe quelle pièce réussie comme pièce actuelle.

TERMINAL ET FENÊTRE 3D
Le terminal reste ouvert à la fin du run : appuie sur Entrée pour le fermer. Pour fermer la fenêtre 3D, ferme-la simplement. Tous les fichiers (code de chaque tentative, STL, journal PicoGK) sont dans le dossier du run, accessible par le bouton « Ouvrir le dossier ».

EN CAS DE PROBLÈME
• « Quota quotidien atteint » : les 50 requêtes gratuites du jour sont épuisées. Attends la remise à zéro, passe à une IA locale, ou ajoute 10 $ de crédits sur OpenRouter une fois pour passer à 1000 requêtes par jour.
• « Data policy » / « confidentialité » : sur openrouter.ai, dans les paramètres de confidentialité, autorise l'usage des modèles gratuits.
• « Aucun modèle utilisable » : les modèles gratuits changent souvent. Clique sur « Vérifier la clé et actualiser », choisis un modèle dans la liste, ou utilise un modèle payant.
• Le code généré ne compile pas : le moteur renvoie l'erreur à l'IA et réessaie. Si ça échoue encore, essaie une IA plus puissante ou décris la pièce plus simplement.
• « Restauration du paquet PicoGK impossible » : lance une fois PicoGK_Examples avec Internet, pour que le paquet PicoGK soit dans le cache.

CONFIDENTIALITÉ DES CLÉS
Les clés saisies dans cette application ne sont enregistrées nulle part : elles sont transmises au moteur par variable d'environnement le temps du run, puis oubliées à la fermeture. Pour ne pas les retaper, définis une variable d'environnement Windows (par exemple avec : setx OPENROUTER_API_KEY "sk-or-...") : l'application la détecte automatiquement.
"""


# ----------------------------------------------------------------------------- utilitaires

def documents_dir() -> Path:
    """Dossier Documents réel (même celui redirigé par OneDrive), comme le fait le moteur."""
    if os.name == "nt":
        try:
            import ctypes
            buf = ctypes.create_unicode_buffer(260)
            if ctypes.windll.shell32.SHGetFolderPathW(None, 5, None, 0, buf) == 0:  # type: ignore[attr-defined]
                return Path(buf.value)
        except Exception:
            pass
    return Path.home() / "Documents"


def settings_path() -> Path:
    base = os.environ.get("APPDATA")
    folder = Path(base) / "CopilotPicoGK" if base else Path.home() / ".copilot_picogk"
    return folder / "settings.json"


def load_settings() -> dict:
    settings = dict(DEFAULTS)
    try:
        settings.update(json.loads(settings_path().read_text(encoding="utf-8")))
    except (OSError, ValueError):
        pass
    return settings


def save_settings(settings: dict) -> None:
    try:
        path = settings_path()
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(settings, indent=2, ensure_ascii=False), encoding="utf-8")
    except OSError:
        pass


def http_json(url: str, key: str | None = None, timeout: float = 15):
    """GET JSON. Retourne (statut HTTP, données, en-têtes). Lève ConnectionError si injoignable."""
    request = urllib.request.Request(url, headers={"User-Agent": "CopilotPicoGK/1.0"})
    if key:
        request.add_header("Authorization", f"Bearer {key}")
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.status, json.loads(response.read().decode("utf-8")), dict(response.headers)
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", "replace")
        try:
            data = json.loads(body)
        except ValueError:
            data = {"raw": body[:300]}
        return error.code, data, dict(error.headers)
    except urllib.error.URLError as error:
        raise ConnectionError(str(error.reason)) from error
    except (TimeoutError, OSError) as error:
        raise ConnectionError(str(error)) from error


def fmt_int(value) -> str:
    try:
        return f"{int(value):,}".replace(",", " ")
    except (TypeError, ValueError):
        return "—"


def fmt_money(value) -> str:
    try:
        return f"{float(value):.4f} $"
    except (TypeError, ValueError):
        return "—"


def parse_utc(text) -> datetime | None:
    try:
        return datetime.strptime(str(text)[:19], "%Y-%m-%dT%H:%M:%S").replace(tzinfo=timezone.utc)
    except (TypeError, ValueError):
        return None


def is_zero(value) -> bool:
    try:
        return float(value) == 0
    except (TypeError, ValueError):
        return False


def is_free_model(model_id: str) -> bool:
    return model_id.endswith(":free") or model_id == "openrouter/free"


def load_runs(workspace: Path) -> list[dict]:
    """Tous les runs (plus récents d'abord), lus dans <workspace>/runs/*/run.json."""
    runs: list[dict] = []
    root = workspace / "runs"
    if not root.is_dir():
        return runs
    for folder in sorted(root.iterdir(), reverse=True):
        report = folder / "run.json"
        if not report.is_file():
            continue
        try:
            data = json.loads(report.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        data["_dir"] = str(folder)
        runs.append(data)
    return runs


def open_path(path: str) -> None:
    if os.name == "nt":
        os.startfile(path)  # type: ignore[attr-defined]
    elif sys.platform == "darwin":
        subprocess.Popen(["open", path])
    else:
        subprocess.Popen(["xdg-open", path])


def make_info(parent, height: int) -> tk.Text:
    """Zone de texte en lecture seule avec défilement. À placer via `widget.container`."""
    container = ttk.Frame(parent)
    widget = tk.Text(container, height=height, wrap="word", state="disabled", relief="flat",
                     background="#f6f8fa", padx=10, pady=8, font=("Segoe UI", 10))
    scroll = ttk.Scrollbar(container, orient="vertical", command=widget.yview)
    widget.configure(yscrollcommand=scroll.set)
    widget.pack(side="left", fill="both", expand=True)
    scroll.pack(side="right", fill="y")
    widget.container = container  # type: ignore[attr-defined]
    widget.tag_configure("title", font=("Segoe UI", 10, "bold"), foreground=COLORS["title"], spacing1=8)
    for tag in ("ok", "warn", "bad", "dim"):
        widget.tag_configure(tag, foreground=COLORS[tag])
    return widget


def show(widget: tk.Text, lines: list[tuple[str, str]]) -> None:
    widget.config(state="normal")
    widget.delete("1.0", "end")
    for text, tag in lines:
        widget.insert("end", text + "\n", tag or ())
    widget.config(state="disabled")


# ----------------------------------------------------------------------------- application

class App(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title(APP_NAME)
        width = min(1060, self.winfo_screenwidth() - 40)
        height = min(800, self.winfo_screenheight() - 90)
        self.geometry(f"{width}x{height}")
        self.minsize(900, 600)

        self.settings = load_settings()
        self.jobs: queue.Queue = queue.Queue()
        self.active: dict | None = None      # run en cours de préparation (avant l'ouverture de la 3D)
        self.viewers: list[subprocess.Popen] = []
        self.current: dict | None = None     # pièce actuelle : {"code", "stl", "spec", "dir"}
        self.history_rows: dict[str, dict] = {}
        self.or_info: dict | None = None

        self._build_style()
        self._build_header()
        self.nb = ttk.Notebook(self)
        self.nb.pack(fill="both", expand=True, padx=10, pady=(0, 4))
        self._build_main_tab()
        self._build_ai_tab()
        self._build_history_tab()
        self._build_settings_tab()
        self._build_help_tab()
        self.status = tk.StringVar(value="Prêt.")
        ttk.Label(self, textvariable=self.status, anchor="w", relief="sunken", padding=(8, 3)).pack(fill="x")

        self._provider_changed()
        self._mode_changed()
        self._refresh_current_label()
        self._refresh_history()
        self._render_usage()
        self.check_install()

        self.bind("<Control-Return>", lambda _e: self.send())
        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self.after(100, self._drain_jobs)
        self.after(1200, self._poll_run)

    # ------------------------------------------------------------------ chemins

    def path_engine(self) -> Path:
        return Path(self.settings.get("engine_dir") or APP_DIR / "CopilotPicoGK.Engine")

    def path_examples(self) -> Path:
        return Path(self.settings.get("examples_dir") or documents_dir() / "PicoGK_Examples-main")

    def path_workspace(self) -> Path:
        return Path(self.settings.get("workspace_dir") or documents_dir() / "CopilotPicoGK")

    def dotnet(self) -> str:
        return self.settings.get("dotnet") or "dotnet"

    # ------------------------------------------------------------------ tâches de fond

    def run_bg(self, work, done) -> None:
        def target() -> None:
            try:
                result, error = work(), None
            except Exception as exc:  # noqa: BLE001 - affiché à l'utilisateur
                result, error = None, exc
            self.jobs.put(lambda: done(result, error))
        threading.Thread(target=target, daemon=True).start()

    def _drain_jobs(self) -> None:
        try:
            while True:
                job = self.jobs.get_nowait()
                try:
                    job()
                except Exception:  # noqa: BLE001 - ne jamais bloquer l'interface
                    traceback.print_exc()
        except queue.Empty:
            pass
        self.after(100, self._drain_jobs)

    # ------------------------------------------------------------------ style et en-tête

    def _build_style(self) -> None:
        style = ttk.Style(self)
        for theme in ("vista", "clam"):
            if theme in style.theme_names():
                style.theme_use(theme)
                break
        style.configure("Accent.TButton", font=("Segoe UI", 11, "bold"), padding=(14, 8))
        style.configure("Title.TLabel", font=("Segoe UI", 17, "bold"))
        style.configure("Dim.TLabel", foreground=COLORS["dim"])
        style.configure("Big.TLabel", font=("Segoe UI", 10, "bold"))

    def _build_header(self) -> None:
        bar = ttk.Frame(self, padding=(14, 10, 14, 6))
        bar.pack(fill="x")
        left = ttk.Frame(bar)
        left.pack(side="left")
        ttk.Label(left, text=APP_NAME, style="Title.TLabel").pack(anchor="w")
        ttk.Label(left, text="Décris une pièce : l'IA écrit le code, PicoGK la construit en 3D.",
                  style="Dim.TLabel").pack(anchor="w")
        right = ttk.Frame(bar)
        right.pack(side="right")
        self.chips: dict[str, ttk.Label] = {}
        for key, text in (("dotnet", "SDK .NET"), ("engine", "Moteur"), ("examples", "Exemples PicoGK")):
            chip = ttk.Label(right, text=f"… {text}", padding=(8, 2))
            chip.pack(side="left", padx=3)
            self.chips[key] = chip

    def _set_chip(self, key: str, label: str, state: str) -> None:
        mark = {"ok": "✔", "bad": "✘", "warn": "!"}.get(state, "…")
        self.chips[key].config(text=f"{mark} {label}", foreground=COLORS.get(state, COLORS["dim"]))

    # ------------------------------------------------------------------ onglet Créer

    def _build_main_tab(self) -> None:
        tab = ttk.Frame(self.nb, padding=10)
        self.nb.add(tab, text="  Créer  ")

        # Les zones du bas sont réservées en premier (pack side="bottom") : si la fenêtre est petite,
        # c'est la zone de saisie qui rétrécit, jamais le suivi ni le bouton Envoyer.
        step1 = ttk.LabelFrame(tab, text=" ①  Quelle IA écrit le code ? ", padding=8)
        step1.pack(side="top", fill="x")
        self.provider = tk.StringVar(value=self.settings["provider"])
        for value, label in PROVIDERS:
            ttk.Radiobutton(step1, text=label, value=value, variable=self.provider,
                            command=self._provider_changed).pack(anchor="w", pady=1)
        line = ttk.Frame(step1)
        line.pack(fill="x", pady=(4, 0))
        ttk.Button(line, text="Détails, quotas et réglages  →", command=self._goto_ai_tab).pack(side="right")
        self.provider_line = ttk.Label(line, wraplength=800, justify="left")
        self.provider_line.pack(side="left", anchor="w")

        card = ttk.LabelFrame(tab, text=" Suivi ", padding=8)
        card.pack(side="bottom", fill="x", pady=(8, 0))
        self.card_vars = {key: tk.StringVar(value="—")
                          for key in ("stage", "attempt", "model", "requests", "tokens", "time")}
        labels = (("stage", "Étape"), ("attempt", "Tentative"), ("model", "IA utilisée"),
                  ("requests", "Requêtes"), ("tokens", "Tokens (envoyés → reçus)"), ("time", "Durée"))
        for index, (key, text) in enumerate(labels):
            cell = ttk.Frame(card)
            cell.grid(row=0, column=index, sticky="w", padx=(0, 22))
            ttk.Label(cell, text=text, style="Dim.TLabel").pack(anchor="w")
            ttk.Label(cell, textvariable=self.card_vars[key], style="Big.TLabel").pack(anchor="w")
        card.columnconfigure(2, weight=1)
        self.card_msg = ttk.Label(card, text="Aucun run pour l'instant.", wraplength=950, justify="left")
        self.card_msg.grid(row=1, column=0, columnspan=6, sticky="w", pady=(6, 0))
        bottom = ttk.Frame(card)
        bottom.grid(row=2, column=0, columnspan=6, sticky="we", pady=(6, 0))
        self.progress = ttk.Progressbar(bottom, mode="indeterminate", length=220)
        self.progress.pack(side="left", padx=(0, 12))
        self.btn_folder = ttk.Button(bottom, text="Ouvrir le dossier du run", state="disabled",
                                     command=lambda: self._open_run_folder(self.last_dir))
        self.btn_folder.pack(side="left")
        self.btn_code = ttk.Button(bottom, text="Voir le code C#", state="disabled",
                                   command=lambda: self._view_code(self.last_code))
        self.btn_code.pack(side="left", padx=8)
        self.last_dir: str | None = None
        self.last_code: str | None = None

        step3 = ttk.Frame(tab)
        step3.pack(side="bottom", fill="x", pady=(8, 0))
        self.send_btn = ttk.Button(step3, text="▶   Envoyer  (ouvre le terminal)", style="Accent.TButton",
                                   command=self.send)
        self.send_btn.pack(side="left")
        ttk.Label(step3, text="  Tentatives max :", style="Dim.TLabel").pack(side="left", padx=(16, 4))
        self.attempts = tk.IntVar(value=int(self.settings.get("max_attempts", 4)))
        ttk.Spinbox(step3, from_=1, to=10, width=4, textvariable=self.attempts).pack(side="left")
        ttk.Label(step3, text="  (Ctrl+Entrée pour envoyer)", style="Dim.TLabel").pack(side="left")

        step2 = ttk.LabelFrame(tab, text=" ②  Que veux-tu créer ? ", padding=8)
        step2.pack(side="top", fill="both", expand=True, pady=(8, 0))
        modebar = ttk.Frame(step2)
        modebar.pack(fill="x")
        self.mode = tk.StringVar(value="new")
        ttk.Radiobutton(modebar, text="Nouvelle pièce", value="new", variable=self.mode,
                        command=self._mode_changed).pack(side="left")
        self.rb_modify = ttk.Radiobutton(modebar, text="Modifier la pièce actuelle", value="modify",
                                         variable=self.mode, command=self._mode_changed)
        self.rb_modify.pack(side="left", padx=18)
        ttk.Button(modebar, text="Oublier la pièce actuelle", command=self._forget_current).pack(side="right")
        self.current_label = ttk.Label(step2, style="Dim.TLabel", wraplength=950)
        self.current_label.pack(anchor="w", pady=(4, 0))

        examples = ttk.Frame(step2)
        examples.pack(side="bottom", fill="x")
        self.mode_hint = ttk.Label(step2, style="Dim.TLabel", wraplength=950)
        self.mode_hint.pack(side="bottom", anchor="w", pady=(4, 0))
        ttk.Label(examples, text="Exemple :", style="Dim.TLabel").pack(side="left")
        self.example_var = tk.StringVar()
        self.example_box = ttk.Combobox(examples, textvariable=self.example_var, state="readonly", width=80)
        self.example_box.pack(side="left", padx=6)
        ttk.Button(examples, text="Insérer", command=self._insert_example).pack(side="left")
        self.spec = tk.Text(step2, height=3, wrap="word", font=("Segoe UI", 11), padx=8, pady=6, undo=True)
        self.spec.pack(fill="both", expand=True, pady=6)

    def _goto_ai_tab(self) -> None:
        self.nb.select(1)
        order = {"openrouter": 0, "ollama": 1, "other": 2}
        self.ai_nb.select(order[self.provider.get()])

    def _insert_example(self) -> None:
        text = self.example_var.get()
        if text:
            self.spec.delete("1.0", "end")
            self.spec.insert("1.0", text)

    def _mode_changed(self) -> None:
        modify = self.mode.get() == "modify"
        examples = EXAMPLES_MODIFY if modify else EXAMPLES_NEW
        self.example_box.config(values=examples)
        self.example_var.set("")
        if modify and not self.current:
            self.mode.set("new")
            modify = False
            messagebox.showinfo(APP_NAME, "Il n'y a pas encore de pièce à modifier : crée-en une d'abord, "
                                          "ou choisis une pièce réussie dans l'onglet « Historique ».")
        self.mode_hint.config(
            text="Décris seulement le changement voulu : l'IA reçoit le code de la pièce actuelle et le modifie."
            if modify else
            "Donne les dimensions en millimètres et la forme voulue. Plus la demande est précise, "
            "meilleur est le résultat. Une idée bien fondée suffit : l'IA fera des hypothèses raisonnables.")

    def _refresh_current_label(self) -> None:
        if self.current:
            spec = self.current["spec"].replace("\n", " ")
            self.current_label.config(
                text=f"Pièce actuelle : « {spec[:90]}{'…' if len(spec) > 90 else ''} »  (run {Path(self.current['dir']).name})")
        else:
            self.current_label.config(text="Aucune pièce actuelle : crée-en une pour pouvoir la modifier ensuite.")

    def _forget_current(self) -> None:
        self.current = None
        self.mode.set("new")
        self._refresh_current_label()
        self._mode_changed()

    def _set_current(self, data: dict, run_dir: str) -> bool:
        code, stl = data.get("finalCode"), data.get("stl")
        if not code or not stl or not Path(code).is_file() or not Path(stl).is_file():
            return False
        self.current = {"code": code, "stl": stl, "spec": data.get("spec", ""), "dir": run_dir}
        self._refresh_current_label()
        return True

    # ------------------------------------------------------------------ onglet IA & quotas

    def _build_ai_tab(self) -> None:
        tab = ttk.Frame(self.nb, padding=12)
        self.nb.add(tab, text="  IA & quotas  ")
        self.ai_nb = ttk.Notebook(tab)
        self.ai_nb.pack(fill="both", expand=True)
        self._build_openrouter_tab()
        self._build_ollama_tab()
        self._build_other_tab()
        usage = ttk.LabelFrame(tab, text=" Consommation de ce programme sur ce PC ", padding=6)
        usage.pack(fill="x", pady=(8, 0))
        self.usage_text = make_info(usage, 5)
        self.usage_text.container.pack(fill="x")

    def _entry_row(self, parent, row: int, label: str, variable, show_char: str | None = None, width: int = 54):
        ttk.Label(parent, text=label).grid(row=row, column=0, sticky="w", pady=3, padx=(0, 8))
        entry = ttk.Entry(parent, textvariable=variable, width=width, show=show_char or "")
        entry.grid(row=row, column=1, sticky="we", pady=3)
        return entry

    def _key_row(self, parent, row: int, variable):
        entry = self._entry_row(parent, row, "Clé API :", variable, show_char="•")
        shown = tk.BooleanVar(value=False)
        ttk.Checkbutton(parent, text="Afficher", variable=shown,
                        command=lambda: entry.config(show="" if shown.get() else "•")).grid(row=row, column=2, padx=6)
        return entry

    def _build_openrouter_tab(self) -> None:
        tab = ttk.Frame(self.ai_nb, padding=10)
        self.ai_nb.add(tab, text="  OpenRouter  ")
        tab.columnconfigure(1, weight=1)
        tab.rowconfigure(4, weight=1)
        self.or_key = tk.StringVar()
        self._key_row(tab, 0, self.or_key)
        self.or_env = ttk.Label(tab, style="Dim.TLabel")
        self.or_env.grid(row=1, column=1, sticky="w")
        self.or_model = tk.StringVar(value=self.settings.get("or_model", "auto"))
        ttk.Label(tab, text="Modèle :").grid(row=2, column=0, sticky="w", pady=3, padx=(0, 8))
        self.or_model_box = ttk.Combobox(tab, textvariable=self.or_model, values=["auto"], width=52)
        self.or_model_box.grid(row=2, column=1, sticky="we", pady=3)
        ttk.Label(tab, text="« auto » = le moteur choisit les meilleurs modèles gratuits du moment. "
                            "Tu peux aussi écrire un modèle, ou plusieurs séparés par des virgules.",
                  style="Dim.TLabel", wraplength=800).grid(row=3, column=1, sticky="w")
        self.or_info_text = make_info(tab, 10)
        self.or_info_text.container.grid(row=4, column=0, columnspan=3, sticky="nsew", pady=(8, 6))
        show(self.or_info_text, [("Clique sur « Vérifier la clé et actualiser » pour voir ton compte, tes crédits, "
                                  "tes quotas et la liste des modèles gratuits du moment.", "dim")])
        buttons = ttk.Frame(tab)
        buttons.grid(row=5, column=0, columnspan=3, sticky="w")
        ttk.Button(buttons, text="Vérifier la clé et actualiser", command=self.refresh_openrouter).pack(side="left")
        for text, url in (("Mes clés ↗", "https://openrouter.ai/keys"),
                          ("Confidentialité ↗", "https://openrouter.ai/settings/privacy"),
                          ("Ajouter des crédits ↗", "https://openrouter.ai/settings/credits"),
                          ("Modèles gratuits ↗", "https://openrouter.ai/models?q=free")):
            ttk.Button(buttons, text=text, command=lambda u=url: webbrowser.open(u)).pack(side="left", padx=4)
        self._update_or_env()

    def _update_or_env(self) -> None:
        if os.environ.get("OPENROUTER_API_KEY"):
            self.or_env.config(text="✔ Clé détectée dans la variable d'environnement OPENROUTER_API_KEY "
                                    "(laisse le champ vide pour l'utiliser).", foreground=COLORS["ok"])
        else:
            self.or_env.config(text="Aucune variable OPENROUTER_API_KEY détectée : colle ta clé ici "
                                    "(elle ne sera pas enregistrée).", foreground=COLORS["warn"])

    def _build_ollama_tab(self) -> None:
        tab = ttk.Frame(self.ai_nb, padding=10)
        self.ai_nb.add(tab, text="  IA locale (Ollama)  ")
        tab.columnconfigure(1, weight=1)
        tab.rowconfigure(4, weight=1)
        self.ol_url = tk.StringVar(value=self.settings.get("ol_url"))
        self._entry_row(tab, 0, "Adresse d'Ollama :", self.ol_url)
        self.ol_model = tk.StringVar(value=self.settings.get("ol_model", ""))
        ttk.Label(tab, text="Modèle installé :").grid(row=1, column=0, sticky="w", pady=3, padx=(0, 8))
        self.ol_model_box = ttk.Combobox(tab, textvariable=self.ol_model, values=[], width=52)
        self.ol_model_box.grid(row=1, column=1, sticky="we", pady=3)
        self.ol_context = tk.IntVar(value=int(self.settings.get("ol_context", 16384)))
        ttk.Label(tab, text="Taille du contexte :").grid(row=2, column=0, sticky="w", pady=3, padx=(0, 8))
        ttk.Spinbox(tab, from_=12288, to=131072, increment=4096, width=10,
                    textvariable=self.ol_context).grid(row=2, column=1, sticky="w", pady=3)
        ttk.Label(tab, text="Plus le contexte est grand, plus l'IA voit d'exemples PicoGK, mais plus elle "
                            "demande de mémoire vidéo. Baisse-le si Ollama est lent (minimum 12288).",
                  style="Dim.TLabel", wraplength=800).grid(row=3, column=1, sticky="w")
        self.ol_info_text = make_info(tab, 10)
        self.ol_info_text.container.grid(row=4, column=0, columnspan=3, sticky="nsew", pady=(8, 6))
        show(self.ol_info_text, [("Clique sur « Détecter Ollama et actualiser » pour voir les modèles installés "
                                  "sur ce PC et ceux qui sont chargés en mémoire.", "dim")])
        buttons = ttk.Frame(tab)
        buttons.grid(row=5, column=0, columnspan=3, sticky="w")
        ttk.Button(buttons, text="Détecter Ollama et actualiser", command=self.refresh_ollama).pack(side="left")
        ttk.Button(buttons, text="Télécharger Ollama ↗",
                   command=lambda: webbrowser.open("https://ollama.com/download")).pack(side="left", padx=4)
        ttk.Button(buttons, text="Bibliothèque de modèles ↗",
                   command=lambda: webbrowser.open("https://ollama.com/library")).pack(side="left")

    def _build_other_tab(self) -> None:
        tab = ttk.Frame(self.ai_nb, padding=10)
        self.ai_nb.add(tab, text="  Autre API  ")
        tab.columnconfigure(1, weight=1)
        tab.rowconfigure(5, weight=1)
        self.ot_preset = tk.StringVar(value=self.settings.get("ot_preset", "Groq"))
        ttk.Label(tab, text="Fournisseur :").grid(row=0, column=0, sticky="w", pady=3, padx=(0, 8))
        preset_box = ttk.Combobox(tab, textvariable=self.ot_preset, values=list(PRESETS), state="readonly", width=30)
        preset_box.grid(row=0, column=1, sticky="w", pady=3)
        preset_box.bind("<<ComboboxSelected>>", lambda _e: self._preset_changed())
        self.ot_url = tk.StringVar(value=self.settings.get("ot_url"))
        self._entry_row(tab, 1, "Adresse de l'API :", self.ot_url)
        self.ot_key = tk.StringVar()
        self._key_row(tab, 2, self.ot_key)
        self.ot_env = ttk.Label(tab, style="Dim.TLabel")
        self.ot_env.grid(row=3, column=1, sticky="w")
        self.ot_model = tk.StringVar(value=self.settings.get("ot_model", ""))
        self._entry_row(tab, 4, "Modèle :", self.ot_model)
        self.ot_info_text = make_info(tab, 10)
        self.ot_info_text.container.grid(row=5, column=0, columnspan=3, sticky="nsew", pady=(8, 6))
        show(self.ot_info_text, [("Compatible avec toute API de type OpenAI (/chat/completions). "
                                  "Le nom du modèle est obligatoire (exemple chez Groq : un modèle « coder » "
                                  "ou « gpt-oss » de leur catalogue). Clique sur « Tester la connexion ».", "dim")])
        buttons = ttk.Frame(tab)
        buttons.grid(row=6, column=0, columnspan=3, sticky="w")
        ttk.Button(buttons, text="Tester la connexion", command=self.refresh_other).pack(side="left")
        self._update_other_env()

    def _preset_changed(self) -> None:
        url, _env = PRESETS.get(self.ot_preset.get(), ("", ""))
        if url:
            self.ot_url.set(url)
        self._update_other_env()

    def _update_other_env(self) -> None:
        _url, env = PRESETS.get(self.ot_preset.get(), ("", ""))
        if env and os.environ.get(env):
            self.ot_env.config(text=f"✔ Clé détectée dans la variable d'environnement {env}.", foreground=COLORS["ok"])
        elif env:
            self.ot_env.config(text=f"Variable habituelle : {env} (non détectée). Colle ta clé ici.",
                               foreground=COLORS["warn"])
        else:
            self.ot_env.config(text="Serveur local : la clé est généralement inutile.", foreground=COLORS["dim"])

    # --- clés

    def or_key_value(self) -> str:
        return self.or_key.get().strip() or os.environ.get("OPENROUTER_API_KEY", "").strip()

    def other_key_value(self) -> str:
        _url, env = PRESETS.get(self.ot_preset.get(), ("", ""))
        return (self.ot_key.get().strip() or (os.environ.get(env, "") if env else "")
                or os.environ.get("COPILOT_API_KEY", "")).strip()

    # --- OpenRouter

    def refresh_openrouter(self) -> None:
        key = self.or_key_value()
        show(self.or_info_text, [("Interrogation d'OpenRouter…", "dim")])
        self.status.set("Interrogation d'OpenRouter…")

        def work():
            out: dict = {"key_status": None}
            if key:
                status, data, _ = http_json(f"{OPENROUTER}/key", key)
                out["key_status"] = status
                out["key"] = data.get("data", data) if isinstance(data, dict) else {}
            _status, data, _ = http_json(f"{OPENROUTER}/models")
            free = []
            for model in data.get("data", []) if isinstance(data, dict) else []:
                pricing = model.get("pricing") or {}
                identifier = model.get("id", "")
                if (identifier and not identifier.startswith("openrouter/")
                        and is_zero(pricing.get("prompt")) and is_zero(pricing.get("completion"))):
                    free.append((identifier, int(model.get("context_length") or 0)))
            out["free"] = sorted(free, key=lambda item: -item[1])
            return out

        def done(result, error):
            self.status.set("Prêt.")
            self.or_info = result
            self._render_openrouter(result, error)
            if result:
                self.or_model_box.config(values=["auto"] + [name for name, _ctx in result["free"]])
            self._provider_changed()

        self.run_bg(work, done)

    def free_requests_today(self) -> int:
        today = datetime.now(timezone.utc).date()
        total = 0
        for run in load_runs(self.path_workspace()):
            started = parse_utc(run.get("startedAt"))
            if started and started.date() == today and run.get("backend") == "openrouter":
                if is_free_model(str(run.get("modelUsed") or "")):
                    total += int(run.get("requests") or 0)
        return total

    def _render_openrouter(self, result, error) -> None:
        lines: list[tuple[str, str]] = []
        if error:
            lines += [(f"Impossible de joindre OpenRouter : {error}", "bad"),
                      ("Vérifie ta connexion Internet.", "dim")]
            show(self.or_info_text, lines)
            return

        info = result.get("key") or {}
        status = result.get("key_status")
        free_tier = None
        lines.append(("Compte et clé", "title"))
        if status is None:
            lines.append(("Aucune clé : colle-la ci-dessus, ou définis la variable d'environnement "
                          "OPENROUTER_API_KEY, puis relance l'application.", "warn"))
        elif status == 200:
            free_tier = info.get("is_free_tier")
            lines.append((f"✔ Clé valide ({info.get('label') or 'sans nom'})", "ok"))
            lines.append((f"Crédits dépensés : {fmt_money(info.get('usage'))} au total · "
                          f"{fmt_money(info.get('usage_daily'))} aujourd'hui (jour UTC) · "
                          f"{fmt_money(info.get('usage_monthly'))} ce mois-ci", ""))
            if info.get("limit") is None:
                lines.append(("Plafond de dépense de la clé : aucun.", ""))
            else:
                lines.append((f"Plafond de la clé : {fmt_money(info.get('limit'))} · "
                              f"restant : {fmt_money(info.get('limit_remaining'))}", ""))
            if free_tier:
                lines.append(("Compte sans achat de crédits : les modèles payants sont indisponibles "
                              "et les modèles gratuits sont limités à 50 requêtes par jour.", "warn"))
            else:
                lines.append(("Des crédits ont déjà été achetés : les modèles payants fonctionnent tant que le "
                              "solde du compte reste positif.", "ok"))
        elif status == 401:
            lines.append(("✘ Clé refusée par OpenRouter (401). Vérifie qu'elle est complète et non révoquée.", "bad"))
        else:
            lines.append((f"Réponse inattendue d'OpenRouter ({status}).", "warn"))

        used = self.free_requests_today()
        reset = (datetime.now(timezone.utc).replace(hour=0, minute=0, second=0, microsecond=0)
                 + timedelta(days=1)).astimezone()
        lines.append(("Quotas des modèles gratuits (:free)", "title"))
        lines.append(("• 20 requêtes par minute (le moteur patiente et réessaie tout seul).", ""))
        if free_tier is True:
            lines.append((f"• Plafond du jour : 50 requêtes · utilisées par ce programme : {used} · "
                          f"restantes (estimation) : {max(0, 50 - used)}", "ok" if used < 50 else "bad"))
        else:
            lines.append((f"• Plafond du jour : 50 requêtes, ou 1000 si tu as acheté au moins 10 $ de crédits · "
                          f"utilisées par ce programme : {used}", ""))
        lines.append((f"• Remise à zéro à minuit UTC, soit {reset.strftime('%H:%M')} heure locale. "
                      "Les « restantes » sont une estimation : seules les requêtes de ce programme sont comptées.", "dim"))
        lines.append(("• Aucun plafond en tokens sur les modèles gratuits. Sur les modèles payants, seuls les "
                      "crédits en dollars comptent.", "dim"))

        free = result.get("free", [])
        lines.append((f"Modèles gratuits disponibles maintenant : {len(free)}", "title"))
        if free:
            for name, context in free[:14]:
                lines.append((f"• {name} — contexte {context // 1000} k tokens", ""))
            if len(free) > 14:
                lines.append((f"… et {len(free) - 14} autres (liste complète dans le menu « Modèle »).", "dim"))
            lines.append(("Le mode « auto » choisit parmi eux ceux qui conviennent le mieux au code.", "dim"))
        else:
            lines.append(("Aucun modèle gratuit trouvé : le catalogue a peut-être changé, ou OpenRouter est injoignable.", "warn"))
        lines.append(("Si une erreur « data policy » apparaît : autorise les modèles gratuits via le bouton "
                      "« Confidentialité ↗ ».", "dim"))
        show(self.or_info_text, lines)

    # --- Ollama

    def refresh_ollama(self) -> None:
        base = self.ol_url.get().strip().rstrip("/")
        show(self.ol_info_text, [("Recherche d'Ollama…", "dim")])
        self.status.set("Recherche d'Ollama…")

        def work():
            out: dict = {}
            _s, tags, _h = http_json(f"{base}/api/tags", timeout=5)
            out["models"] = tags.get("models", []) if isinstance(tags, dict) else []
            try:
                _s, version, _h = http_json(f"{base}/api/version", timeout=5)
                out["version"] = version.get("version", "?")
            except ConnectionError:
                out["version"] = "?"
            try:
                _s, running, _h = http_json(f"{base}/api/ps", timeout=5)
                out["running"] = running.get("models", []) if isinstance(running, dict) else []
            except ConnectionError:
                out["running"] = []
            return out

        def done(result, error):
            self.status.set("Prêt.")
            lines: list[tuple[str, str]] = []
            if error:
                lines += [(f"✘ Ollama ne répond pas sur {base} ({error}).", "bad"),
                          ("Installe-le avec le bouton « Télécharger Ollama ↗ », puis lance l'application Ollama "
                           "(elle reste en arrière-plan, près de l'horloge).", "dim")]
                show(self.ol_info_text, lines)
                return
            lines.append((f"✔ Ollama répond (version {result['version']}).", "ok"))
            models = result["models"]
            lines.append((f"Modèles installés : {len(models)}", "title"))
            names = []
            for model in models:
                names.append(model.get("name", "?"))
                details = model.get("details") or {}
                size = float(model.get("size") or 0) / 1e9
                lines.append((f"• {model.get('name')} — {size:.1f} Go sur le disque — "
                              f"{details.get('parameter_size', '?')} de paramètres — {details.get('quantization_level', '?')}", ""))
            if not models:
                lines.append(("Aucun modèle installé. Dans un terminal : ollama pull qwen2.5-coder:7b "
                              "(8 Go de mémoire vidéo), qwen2.5-coder:14b (16 Go) ou qwen3-coder:30b (24 Go et plus).", "warn"))
            lines.append(("Chargés en mémoire en ce moment", "title"))
            if result["running"]:
                for model in result["running"]:
                    vram = float(model.get("size_vram") or 0) / 1e9
                    lines.append((f"• {model.get('name')} — {vram:.1f} Go en mémoire vidéo", ""))
            else:
                lines.append(("Aucun (un modèle se charge au premier envoi, ce qui prend quelques secondes).", "dim"))
            lines.append(("Quotas", "title"))
            lines.append(("Aucun : tout tourne sur ton PC, sans clé ni Internet. La limite, c'est ta mémoire vidéo "
                          "et la taille du contexte réglée ci-dessus.", "dim"))
            show(self.ol_info_text, lines)
            self.ol_model_box.config(values=names)
            if names and not self.ol_model.get():
                coder = [name for name in names if "coder" in name.lower()]
                self.ol_model.set((coder or names)[0])
            self._provider_changed()

        self.run_bg(work, done)

    # --- Autre API

    def refresh_other(self) -> None:
        base = self.ot_url.get().strip().rstrip("/")
        key = self.other_key_value()
        if not base:
            show(self.ot_info_text, [("Renseigne d'abord l'adresse de l'API.", "warn")])
            return
        show(self.ot_info_text, [("Test de la connexion…", "dim")])
        self.status.set("Test de la connexion…")

        def work():
            return http_json(f"{base}/models", key or None)

        def done(result, error):
            self.status.set("Prêt.")
            lines: list[tuple[str, str]] = []
            if error:
                lines += [(f"✘ Impossible de joindre {base} : {error}", "bad")]
                show(self.ot_info_text, lines)
                return
            status, data, headers = result
            if status == 200:
                models = [m.get("id", "?") for m in data.get("data", [])] if isinstance(data, dict) else []
                lines.append((f"✔ Connexion réussie — {len(models)} modèle(s) listé(s).", "ok"))
                for name in models[:30]:
                    lines.append((f"• {name}", ""))
                if len(models) > 30:
                    lines.append((f"… et {len(models) - 30} autres.", "dim"))
            elif status in (401, 403):
                lines.append((f"✘ Clé refusée ({status}).", "bad"))
            elif status == 404:
                lines.append(("Ce fournisseur ne liste pas ses modèles (/models) : ce n'est pas bloquant, "
                              "écris simplement le nom du modèle à la main.", "warn"))
            else:
                lines.append((f"Réponse inattendue ({status}).", "warn"))
            limits = {k.lower(): v for k, v in headers.items() if k.lower().startswith("x-ratelimit")}
            lines.append(("Limites annoncées par le fournisseur", "title"))
            if limits:
                for name, value in limits.items():
                    lines.append((f"• {name} : {value}", ""))
            else:
                lines.append(("Aucune pour l'instant. Si le fournisseur en annonce (Groq par exemple), elles s'affichent "
                              "dans le terminal après chaque requête, et dans l'historique.", "dim"))
            show(self.ot_info_text, lines)

        self.run_bg(work, done)

    # --- résumé du fournisseur choisi (onglet Créer)

    def _provider_changed(self) -> None:
        provider = self.provider.get()
        if provider == "openrouter":
            key = "clé détectée ✔" if self.or_key_value() else "clé manquante ✘"
            nbfree = f" · {len(self.or_info['free'])} modèles gratuits disponibles" if self.or_info else ""
            text = f"OpenRouter · modèle : {self.or_model.get() or 'auto'} · {key}{nbfree}"
        elif provider == "ollama":
            model = self.ol_model.get() or "aucun modèle choisi (clique sur « Détecter Ollama »)"
            text = f"IA locale (Ollama) · {model} · contexte {self.ol_context.get()} tokens · aucun quota"
        else:
            key = "clé renseignée ✔" if self.other_key_value() else "pas de clé"
            text = (f"{self.ot_preset.get()} · {self.ot_url.get() or 'adresse manquante'} · "
                    f"modèle : {self.ot_model.get() or 'à renseigner ✘'} · {key}")
        self.provider_line.config(text=text)

    # ------------------------------------------------------------------ onglet Historique

    def _build_history_tab(self) -> None:
        tab = ttk.Frame(self.nb, padding=12)
        self.nb.add(tab, text="  Historique  ")
        ttk.Label(tab, text="Tous tes runs. Sélectionne une pièce réussie pour la modifier, voir son code "
                            "ou ouvrir son dossier.", style="Dim.TLabel").pack(anchor="w")
        columns = ("date", "mode", "ia", "statut", "tent", "requetes", "tokens", "desc")
        titles = ("Date", "Type", "IA utilisée", "Statut", "Tent.", "Req.", "Tokens env. → reçus", "Demande")
        widths = (100, 72, 225, 92, 50, 45, 150, 240)
        frame = ttk.Frame(tab)
        frame.pack(fill="both", expand=True, pady=6)
        self.tree = ttk.Treeview(frame, columns=columns, show="headings", selectmode="browse")
        for column, title, width in zip(columns, titles, widths):
            self.tree.heading(column, text=title)
            self.tree.column(column, width=width, anchor="w", stretch=(column == "desc"))
        scroll = ttk.Scrollbar(frame, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=scroll.set)
        self.tree.pack(side="left", fill="both", expand=True)
        scroll.pack(side="right", fill="y")
        for tag in ("ok", "bad", "warn"):
            self.tree.tag_configure(tag, foreground=COLORS[tag])
        buttons = ttk.Frame(tab)
        buttons.pack(fill="x")
        ttk.Button(buttons, text="Utiliser comme pièce actuelle", command=self._history_use).pack(side="left")
        ttk.Button(buttons, text="Voir le code C#", command=self._history_code).pack(side="left", padx=6)
        ttk.Button(buttons, text="Ouvrir le dossier", command=self._history_folder).pack(side="left")
        ttk.Button(buttons, text="Actualiser", command=self._refresh_history).pack(side="right")

    def _refresh_history(self) -> None:
        self.tree.delete(*self.tree.get_children())
        self.history_rows = {}
        for run in load_runs(self.path_workspace()):
            started = parse_utc(run.get("startedAt"))
            when = started.astimezone().strftime("%d/%m %H:%M") if started else "?"
            text, tag = STATUS_TEXT.get(run.get("status", ""), ("?", ""))
            spec = str(run.get("spec", "")).replace("\n", " ")
            ia = run.get("modelUsed") or run.get("backend") or "—"
            row = (when, "Modif." if run.get("mode") == "modify" else "Nouvelle", ia, text,
                   f"{run.get('attempt', 0)}/{run.get('maxAttempts', 0)}", fmt_int(run.get("requests", 0)),
                   f"{fmt_int(run.get('promptTokens', 0))} → {fmt_int(run.get('completionTokens', 0))}",
                   spec[:80] + ("…" if len(spec) > 80 else ""))
            iid = self.tree.insert("", "end", values=row, tags=(tag,) if tag else ())
            self.history_rows[iid] = run

    def _selected_run(self) -> dict | None:
        selection = self.tree.selection()
        if not selection:
            messagebox.showinfo(APP_NAME, "Sélectionne d'abord une ligne dans la liste.")
            return None
        return self.history_rows.get(selection[0])

    def _history_use(self) -> None:
        run = self._selected_run()
        if not run:
            return
        if run.get("status") != "success" or not self._set_current(run, run["_dir"]):
            messagebox.showwarning(APP_NAME, "Seule une pièce réussie, dont le code et le STL existent encore, "
                                             "peut devenir la pièce actuelle.")
            return
        self.mode.set("modify")
        self._mode_changed()
        self.nb.select(0)

    def _history_code(self) -> None:
        run = self._selected_run()
        if run:
            self._view_code(run.get("finalCode"))

    def _history_folder(self) -> None:
        run = self._selected_run()
        if run:
            self._open_run_folder(run["_dir"])

    # ------------------------------------------------------------------ onglet Paramètres

    def _build_settings_tab(self) -> None:
        tab = ttk.Frame(self.nb, padding=12)
        self.nb.add(tab, text="  Paramètres  ")
        tab.columnconfigure(1, weight=1)
        self.set_vars = {
            "engine_dir": tk.StringVar(value=str(self.path_engine())),
            "examples_dir": tk.StringVar(value=str(self.path_examples())),
            "workspace_dir": tk.StringVar(value=str(self.path_workspace())),
            "dotnet": tk.StringVar(value=self.dotnet()),
        }
        rows = (("engine_dir", "Dossier du moteur (CopilotPicoGK.Engine) :", True),
                ("examples_dir", "Dossier PicoGK_Examples :", True),
                ("workspace_dir", "Dossier de travail (runs, résultats) :", True),
                ("dotnet", "Commande dotnet :", False))
        for index, (key, label, browse) in enumerate(rows):
            ttk.Label(tab, text=label).grid(row=index, column=0, sticky="w", pady=4, padx=(0, 8))
            ttk.Entry(tab, textvariable=self.set_vars[key]).grid(row=index, column=1, sticky="we", pady=4)
            if browse:
                ttk.Button(tab, text="Parcourir…",
                           command=lambda k=key: self._browse(k)).grid(row=index, column=2, padx=6)
        buttons = ttk.Frame(tab)
        buttons.grid(row=len(rows), column=0, columnspan=3, sticky="w", pady=8)
        ttk.Button(buttons, text="Enregistrer et vérifier l'installation", command=self._apply_settings).pack(side="left")
        ttk.Button(buttons, text="Rétablir les valeurs par défaut", command=self._reset_paths).pack(side="left", padx=8)
        self.install_text = make_info(tab, 12)
        self.install_text.container.grid(row=len(rows) + 1, column=0, columnspan=3, sticky="nsew")
        tab.rowconfigure(len(rows) + 1, weight=1)

    def _browse(self, key: str) -> None:
        folder = filedialog.askdirectory(initialdir=self.set_vars[key].get() or str(documents_dir()))
        if folder:
            self.set_vars[key].set(folder)

    def _reset_paths(self) -> None:
        for key in ("engine_dir", "examples_dir", "workspace_dir"):
            self.settings[key] = ""
        self.set_vars["engine_dir"].set(str(self.path_engine()))
        self.set_vars["examples_dir"].set(str(self.path_examples()))
        self.set_vars["workspace_dir"].set(str(self.path_workspace()))
        self.set_vars["dotnet"].set("dotnet")
        self._apply_settings()

    def _apply_settings(self) -> None:
        for key, variable in self.set_vars.items():
            self.settings[key] = variable.get().strip()
        self.persist()
        self.check_install()
        self._refresh_history()
        self._render_usage()

    def persist(self) -> None:
        """Enregistre les réglages (jamais les clés API)."""
        try:
            self.settings.update({
                "provider": self.provider.get(),
                "max_attempts": int(self.attempts.get()),
                "or_model": self.or_model.get(), "ol_url": self.ol_url.get(),
                "ol_model": self.ol_model.get(), "ol_context": int(self.ol_context.get()),
                "ot_preset": self.ot_preset.get(), "ot_url": self.ot_url.get(), "ot_model": self.ot_model.get(),
            })
        except tk.TclError:
            pass
        save_settings(self.settings)

    def check_install(self) -> None:
        engine, examples, workspace, dotnet = (self.path_engine(), self.path_examples(),
                                               self.path_workspace(), self.dotnet())

        def work():
            result: dict = {"dotnet_path": shutil.which(dotnet), "sdks": []}
            if result["dotnet_path"]:
                try:
                    out = subprocess.run([dotnet, "--list-sdks"], capture_output=True, text=True, timeout=30)
                    result["sdks"] = [line.strip() for line in out.stdout.splitlines() if line.strip()]
                except (OSError, subprocess.SubprocessError):
                    pass
            result["engine_ok"] = any(engine.glob("*.csproj")) if engine.is_dir() else False
            result["examples_count"] = len(list(examples.rglob("*.cs"))) if examples.is_dir() else 0
            return result

        def done(result, _error):
            if not result:
                return
            sdk9 = any(line.startswith("9.") for line in result["sdks"])
            lines: list[tuple[str, str]] = [("Vérification de l'installation", "title")]
            if not result["dotnet_path"]:
                self._set_chip("dotnet", "SDK .NET", "bad")
                lines.append(("✘ La commande « dotnet » est introuvable. Installe le SDK .NET 9 : "
                              "winget install Microsoft.DotNet.SDK.9, puis relance l'application.", "bad"))
            elif not sdk9:
                self._set_chip("dotnet", "SDK .NET 9 ?", "warn")
                lines.append((f"! dotnet est présent mais aucun SDK 9.x n'est listé ({', '.join(result['sdks']) or 'aucun'}).", "warn"))
            else:
                self._set_chip("dotnet", "SDK .NET 9", "ok")
                lines.append((f"✔ SDK .NET : {', '.join(result['sdks'])}", "ok"))
            if result["engine_ok"]:
                self._set_chip("engine", "Moteur", "ok")
                lines.append((f"✔ Moteur trouvé : {engine}", "ok"))
            else:
                self._set_chip("engine", "Moteur", "bad")
                lines.append((f"✘ Moteur introuvable dans {engine} (il doit contenir CopilotPicoGK.Engine.csproj). "
                              "Place l'application à côté du dossier CopilotPicoGK.Engine, ou indique son chemin ci-dessus.", "bad"))
            if result["examples_count"]:
                self._set_chip("examples", "Exemples PicoGK", "ok")
                lines.append((f"✔ PicoGK_Examples : {result['examples_count']} fichiers .cs dans {examples}", "ok"))
            else:
                self._set_chip("examples", "Exemples PicoGK", "bad")
                lines.append((f"✘ Aucun fichier .cs dans {examples}. Télécharge PicoGK_Examples et indique son dossier.", "bad"))
            try:
                workspace.mkdir(parents=True, exist_ok=True)
                lines.append((f"✔ Dossier de travail : {workspace}", "ok"))
            except OSError as error:
                lines.append((f"✘ Dossier de travail inutilisable ({error}).", "bad"))
            show(self.install_text, lines)

        self.run_bg(work, done)

    # ------------------------------------------------------------------ onglet Aide

    def _build_help_tab(self) -> None:
        tab = ttk.Frame(self.nb, padding=12)
        self.nb.add(tab, text="  Aide  ")
        text = ScrolledText(tab, wrap="word", font=("Segoe UI", 10), padx=12, pady=10, relief="flat",
                            background="#f6f8fa")
        text.pack(fill="both", expand=True)
        text.tag_configure("h", font=("Segoe UI", 10, "bold"), foreground=COLORS["title"], spacing1=10)
        for line in HELP_TEXT.splitlines():
            is_heading = line.isupper() and len(line) > 3
            text.insert("end", line + "\n", "h" if is_heading else ())
        text.config(state="disabled")

    # ------------------------------------------------------------------ consommation

    def _render_usage(self) -> None:
        runs = load_runs(self.path_workspace())
        today = datetime.now(timezone.utc).date()

        def total(selection: list[dict]) -> tuple[int, int, int, int, float]:
            return (len(selection), sum(int(r.get("requests") or 0) for r in selection),
                    sum(int(r.get("promptTokens") or 0) for r in selection),
                    sum(int(r.get("completionTokens") or 0) for r in selection),
                    sum(float(r.get("cost") or 0) for r in selection))

        def describe(label: str, selection: list[dict]) -> tuple[str, str]:
            count, requests, prompt, completion, cost = total(selection)
            return (f"{label} : {count} run(s) · {fmt_int(requests)} requête(s) · "
                    f"{fmt_int(prompt)} tokens envoyés → {fmt_int(completion)} reçus · coût {fmt_money(cost)}", "")

        todays = [r for r in runs if (parse_utc(r.get("startedAt")) or datetime.min.replace(tzinfo=timezone.utc)).date() == today]
        lines = [describe("Aujourd'hui (jour UTC)", todays), describe("Depuis le début", runs)]
        by_backend: dict[str, list[dict]] = {}
        for run in runs:
            by_backend.setdefault(PROVIDER_NAMES.get(run.get("backend", ""), run.get("backend") or "?")
                                  if run.get("backend") != "openai" else "Autre API", []).append(run)
        for name, selection in sorted(by_backend.items()):
            lines.append(describe(f"  • {name}", selection))
        succeeded = sum(1 for r in runs if r.get("status") == "success")
        lines.append((f"Réussites : {succeeded} sur {len(runs)} run(s).", "dim"))
        if not runs:
            lines = [("Aucun run enregistré pour l'instant : les chiffres apparaîtront après ton premier envoi.", "dim")]
        show(self.usage_text, lines)

    # ------------------------------------------------------------------ lancement d'un run

    def _launch_options(self):
        """Retourne (arguments du moteur, variables d'environnement) ou lève ValueError (message affichable)."""
        provider = self.provider.get()
        env = dict(os.environ)
        args: list[str] = []
        if provider == "openrouter":
            key = self.or_key_value()
            if not key:
                raise ValueError("Clé OpenRouter manquante : colle-la dans l'onglet « IA & quotas », "
                                 "ou définis la variable d'environnement OPENROUTER_API_KEY.")
            env["OPENROUTER_API_KEY"] = key
            args = ["--backend", "openrouter", "--model", self.or_model.get().strip() or "auto"]
        elif provider == "ollama":
            model = self.ol_model.get().strip()
            if not model:
                raise ValueError("Aucun modèle Ollama choisi : va dans « IA & quotas », onglet « IA locale », "
                                 "et clique sur « Détecter Ollama et actualiser ».")
            args = ["--backend", "ollama", "--model", model, "--base-url", self.ol_url.get().strip(),
                    "--context", str(int(self.ol_context.get()))]
        else:
            url, model = self.ot_url.get().strip(), self.ot_model.get().strip()
            if not url or not model:
                raise ValueError("Pour une autre API, renseigne l'adresse et le nom du modèle "
                                 "dans « IA & quotas », onglet « Autre API ».")
            env["COPILOT_API_KEY"] = self.other_key_value() or "none"
            args = ["--backend", "openai", "--base-url", url, "--model", model, "--api-key-env", "COPILOT_API_KEY"]
        return args, env

    def send(self) -> None:
        if self.active:
            messagebox.showinfo(APP_NAME, "Un run est déjà en cours : attends que la fenêtre 3D s'ouvre "
                                          "(ou que le run se termine) avant d'envoyer une nouvelle demande.")
            return
        spec = self.spec.get("1.0", "end").strip()
        if not spec:
            messagebox.showinfo(APP_NAME, "Décris d'abord la pièce (étape ②).")
            return
        modify = self.mode.get() == "modify"
        if modify and not self.current:
            messagebox.showinfo(APP_NAME, "Aucune pièce à modifier pour l'instant.")
            return
        try:
            provider_args, env = self._launch_options()
        except ValueError as error:
            messagebox.showwarning(APP_NAME, str(error))
            return

        engine, examples, workspace = self.path_engine(), self.path_examples(), self.path_workspace()
        if not any(engine.glob("*.csproj")):
            messagebox.showerror(APP_NAME, f"Moteur introuvable dans :\n{engine}\n\nVoir l'onglet « Paramètres ».")
            return

        run_dir = workspace / "runs" / datetime.now().strftime("%Y%m%d-%H%M%S")
        run_dir.mkdir(parents=True, exist_ok=True)
        spec_file = run_dir / "spec.txt"
        spec_file.write_text(spec, encoding="utf-8")
        command = [self.dotnet(), "run", "--project", str(engine), "-c", "Release", "--",
                   "--spec-file", str(spec_file), "--examples", str(examples), "--workspace", str(workspace),
                   "--run-dir", str(run_dir), "--attempts", str(int(self.attempts.get())), "--pause", *provider_args]
        if modify:
            command += ["--base-code", self.current["code"]]  # type: ignore[index]

        try:
            process = subprocess.Popen(command, env=env, cwd=str(engine),
                                       creationflags=getattr(subprocess, "CREATE_NEW_CONSOLE", 0))
        except FileNotFoundError:
            messagebox.showerror(APP_NAME, "La commande « dotnet » est introuvable.\n\n"
                                           "Installe le SDK .NET 9 (winget install Microsoft.DotNet.SDK.9) "
                                           "ou indique son chemin dans « Paramètres ».")
            return
        except OSError as error:
            messagebox.showerror(APP_NAME, f"Impossible de lancer le moteur : {error}")
            return

        self.persist()
        self.active = {"proc": process, "dir": str(run_dir), "t0": time.time(), "spec": spec}
        self.last_dir, self.last_code = str(run_dir), None
        self.btn_folder.config(state="normal")
        self.btn_code.config(state="disabled")
        self.send_btn.config(state="disabled")
        self.progress.start(12)
        self.card_msg.config(text="Un terminal vient de s'ouvrir : tu y vois l'avancement et les erreurs en direct.",
                             foreground=COLORS["warn"])
        self.status.set("Run en cours — regarde le terminal.")

    def _poll_run(self) -> None:
        try:
            before = len(self.viewers)
            self.viewers = [p for p in self.viewers if p.poll() is None]
            if len(self.viewers) < before:
                # Une fenêtre 3D vient d'être fermée : le run est terminé, on met les chiffres à jour.
                self._refresh_history()
                self._render_usage()
                if self.or_info is not None:
                    self._render_openrouter(self.or_info, None)
            if self.active:
                self._poll_active()
            suffix = f" · {len(self.viewers)} fenêtre(s) 3D ouverte(s)" if self.viewers else ""
            if not self.active:
                self.status.set(f"Prêt.{suffix}")
        except Exception:  # noqa: BLE001 - ne jamais arrêter le suivi
            traceback.print_exc()
        finally:
            self.after(1200, self._poll_run)

    def _read_report(self, run_dir: str) -> dict | None:
        try:
            return json.loads((Path(run_dir) / "run.json").read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return None

    def _poll_active(self) -> None:
        active = self.active
        assert active is not None
        data = self._read_report(active["dir"]) or {}
        elapsed = int(time.time() - active["t0"])
        self._update_card(data, elapsed)
        status, stage = data.get("status", "running"), data.get("stage", "init")
        self.last_code = data.get("finalCode") or self.last_code
        if self.last_code:
            self.btn_code.config(state="normal")

        finished = status in ("success", "failed", "error")
        died = active["proc"].poll() is not None
        stl_ready = bool(data.get("stl")) and Path(data["stl"]).is_file()

        if stage == "exécution" and stl_ready and not finished:
            # La fenêtre 3D est ouverte et la pièce existe : on peut enchaîner une modification.
            self._set_current(data, active["dir"])
            self.viewers.append(active["proc"])
            self._end_active(ok=True, message="✔ Pièce prête : la fenêtre 3D PicoGK est ouverte. "
                                              "Pour la modifier, décris le changement et renvoie.")
            self.mode.set("modify")
            self._mode_changed()
            self.spec.delete("1.0", "end")
        elif finished:
            ok = status == "success"
            if ok:
                self._set_current(data, active["dir"])
            message = ("✔ Terminé : la pièce a été construite." if ok else
                       "✘ Échec : " + (data.get("error") or "le code n'a pas produit de pièce. "
                                                              "Regarde le terminal et le dossier du run."))
            self._end_active(ok=ok, message=message)
        elif died:
            self._end_active(ok=False, message="✘ Le terminal a été fermé avant la fin du run.")

    def _end_active(self, ok: bool, message: str) -> None:
        self.active = None
        self.progress.stop()
        self.send_btn.config(state="normal")
        self.card_msg.config(text=message, foreground=COLORS["ok"] if ok else COLORS["bad"])
        self.status.set("Prêt.")
        self._refresh_history()
        self._render_usage()
        if self.or_info is not None:
            self._render_openrouter(self.or_info, None)

    def _update_card(self, data: dict, elapsed: int) -> None:
        stage = data.get("stage", "init")
        self.card_vars["stage"].set(STAGES.get(stage, stage))
        self.card_vars["attempt"].set(f"{data.get('attempt', 0)} / {data.get('maxAttempts', '?')}")
        self.card_vars["model"].set(data.get("modelUsed") or "—")
        self.card_vars["requests"].set(fmt_int(data.get("requests", 0)))
        self.card_vars["tokens"].set(f"{fmt_int(data.get('promptTokens', 0))} → {fmt_int(data.get('completionTokens', 0))}")
        self.card_vars["time"].set(f"{elapsed // 60}:{elapsed % 60:02d}")
        limits = data.get("rateLimit") or {}
        if limits and self.active:
            self.card_msg.config(text="Limites annoncées par le fournisseur : "
                                      + " · ".join(f"{k}={v}" for k, v in limits.items()))

    # ------------------------------------------------------------------ fichiers

    def _open_run_folder(self, folder: str | None) -> None:
        if folder and Path(folder).is_dir():
            open_path(folder)
        else:
            messagebox.showinfo(APP_NAME, "Dossier introuvable.")

    def _view_code(self, path: str | None) -> None:
        if not path or not Path(path).is_file():
            messagebox.showinfo(APP_NAME, "Pas encore de code disponible pour ce run.")
            return
        window = tk.Toplevel(self)
        window.title(f"Code C# — {Path(path).parent.name}/{Path(path).name}")
        window.geometry("820x640")
        text = ScrolledText(window, wrap="none", font=("Consolas", 10))
        text.pack(fill="both", expand=True)
        text.insert("1.0", Path(path).read_text(encoding="utf-8"))
        text.config(state="disabled")
        bar = ttk.Frame(window, padding=6)
        bar.pack(fill="x")

        def copy() -> None:
            self.clipboard_clear()
            self.clipboard_append(Path(path).read_text(encoding="utf-8"))

        ttk.Button(bar, text="Copier le code", command=copy).pack(side="left")
        ttk.Label(bar, text=str(path), style="Dim.TLabel").pack(side="left", padx=10)

    def _on_close(self) -> None:
        if self.active and self.active["proc"].poll() is None:
            if not messagebox.askyesno(APP_NAME, "Un run est en cours dans le terminal. Fermer l'application "
                                                 "ne l'arrête pas. Fermer quand même ?"):
                return
        self.persist()
        self.destroy()


def main() -> None:
    App().mainloop()


if __name__ == "__main__":
    main()
