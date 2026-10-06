@echo off
cd /d "%~dp0"
where pyw >nul 2>nul && (start "" pyw -3 copilot_picogk_app.py & exit /b 0)
where pythonw >nul 2>nul && (start "" pythonw copilot_picogk_app.py & exit /b 0)
echo Python est introuvable sur ce PC.
echo Installe-le avec cette commande, puis relance ce fichier :
echo     winget install Python.Python.3.12
pause
