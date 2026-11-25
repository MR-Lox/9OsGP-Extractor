using System.Collections.Generic;
using UnityEngine;

namespace RaceBoot
{
    // Languages of the mod's own interface: the same five as Takeover 2's options menu.
    // Texts are written in French in the code; L.T() returns the translation for the chosen language.
    public static class L
    {
        public static readonly string[] Codes = { "en", "es", "fr", "de", "pt" };
        public static readonly string[] Names = { "English", "Español", "Français", "Deutsch", "Português BR" };

        public static string Code = "fr";

        public static string Default()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Portuguese: return "pt";
                default: return "en";
            }
        }

        public static string T(string fr)
        {
            if (Code == "fr" || !Table.TryGetValue(fr, out var t)) return fr;
            switch (Code)
            {
                case "en": return t[0];
                case "es": return t[1];
                case "de": return t[2];
                case "pt": return t[3];
                default: return fr;
            }
        }

        // French -> English, Spanish, German, Portuguese (Brazil).
        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "arrêté", new[] { "stopped", "detenido", "angehalten", "parado" } },
            { "F1 : options   —   C / bouton Y : changer de vue", new[] { "F1: options   —   C / Y button: change view", "F1: opciones   —   C / botón Y: cambiar de vista", "F1: Optionen   —   C / Y-Taste: Ansicht wechseln", "F1: opções   —   C / botão Y: mudar a visão" } },
            { "Affichage", new[] { "Display", "Pantalla", "Anzeige", "Tela" } },
            { "Graphismes", new[] { "Graphics", "Gráficos", "Grafik", "Gráficos" } },
            { "Caméra", new[] { "Camera", "Cámara", "Kamera", "Câmera" } },
            { "HUD", new[] { "HUD", "HUD", "HUD", "HUD" } },
            { "Contrôles", new[] { "Controls", "Controles", "Steuerung", "Controles" } },
            { "Fermer (F1 / Échap)", new[] { "Close (F1 / Esc)", "Cerrar (F1 / Esc)", "Schließen (F1 / Esc)", "Fechar (F1 / Esc)" } },
            { "Quitter le jeu", new[] { "Quit game", "Salir del juego", "Spiel beenden", "Sair do jogo" } },
            { "Oui", new[] { "Yes", "Sí", "Ja", "Sim" } },
            { "Non", new[] { "No", "No", "Nein", "Não" } },
            { "Mode d'affichage", new[] { "Display mode", "Modo de pantalla", "Anzeigemodus", "Modo de exibição" } },
            { "Plein écran", new[] { "Fullscreen", "Pantalla completa", "Vollbild", "Tela cheia" } },
            { "Plein écran fenêtré", new[] { "Borderless window", "Ventana sin bordes", "Randloses Fenster", "Janela sem bordas" } },
            { "Fenêtré", new[] { "Windowed", "Ventana", "Fenster", "Janela" } },
            { "Résolution", new[] { "Resolution", "Resolución", "Auflösung", "Resolução" } },
            { "Appliquer", new[] { "Apply", "Aplicar", "Anwenden", "Aplicar" } },
            { "Actuelle : ", new[] { "Current: ", "Actual: ", "Aktuell: ", "Atual: " } },
            { "Synchronisation verticale (V-Sync)", new[] { "Vertical sync (V-Sync)", "Sincronización vertical (V-Sync)", "Vertikale Synchronisation (V-Sync)", "Sincronização vertical (V-Sync)" } },
            { "Vue", new[] { "View", "Vista", "Ansicht", "Visão" } },
            { "En course : touche C (clavier) ou bouton Y / Select (manette) pour changer de vue.", new[] { "In race: C key (keyboard) or Y / Select button (gamepad) to change view.", "En carrera: tecla C (teclado) o botón Y / Select (mando) para cambiar de vista.", "Im Rennen: Taste C (Tastatur) oder Y- / Select-Taste (Gamepad) zum Wechseln der Ansicht.", "Na corrida: tecla C (teclado) ou botão Y / Select (controle) para mudar a visão." } },
            { "Replay de la course sur l'écran « PRESS START »", new[] { "Race replay on the \"PRESS START\" screen", "Repetición de la carrera en la pantalla \"PRESS START\"", "Rennwiederholung auf dem „PRESS START“-Bildschirm", "Replay da corrida na tela \"PRESS START\"" } },
            { "F9 sur l'écran « PRESS START » : revoir le dernier replay (gardé même après avoir quitté le jeu).", new[] { "F9 on the \"PRESS START\" screen: watch the last replay again (kept even after quitting the game).", "F9 en la pantalla \"PRESS START\": volver a ver la última repetición (se conserva al salir del juego).", "F9 auf dem „PRESS START“-Bildschirm: letzte Wiederholung erneut ansehen (bleibt auch nach Spielende erhalten).", "F9 na tela \"PRESS START\": rever o último replay (mantido mesmo após sair do jogo)." } },
            { "Réglages des vues intérieures", new[] { "Interior view settings", "Ajustes de las vistas interiores", "Einstellungen der Innenansichten", "Ajustes das visões internas" } },
            { "Champ de vision", new[] { "Field of view", "Campo de visión", "Sichtfeld", "Campo de visão" } },
            { "Hauteur", new[] { "Height", "Altura", "Höhe", "Altura" } },
            { "Avant / arrière", new[] { "Forward / back", "Adelante / atrás", "Vor / zurück", "Frente / trás" } },
            { "Inclinaison", new[] { "Tilt", "Inclinación", "Neigung", "Inclinação" } },
            { "Valeurs par défaut", new[] { "Default values", "Valores por defecto", "Standardwerte", "Valores padrão" } },
            { "Éléments affichés", new[] { "Displayed elements", "Elementos mostrados", "Angezeigte Elemente", "Elementos exibidos" } },
            { "Mini-carte (cachée par les développeurs)", new[] { "Minimap (hidden by the developers)", "Minimapa (oculto por los desarrolladores)", "Minikarte (von den Entwicklern ausgeblendet)", "Minimapa (oculto pelos desenvolvedores)" } },
            { "Compte-tours et vitesse", new[] { "Rev counter and speed", "Cuentarrevoluciones y velocidad", "Drehzahlmesser und Geschwindigkeit", "Conta-giros e velocidade" } },
            { "Taille", new[] { "Size", "Tamaño", "Größe", "Tamanho" } },
            { "Position horizontale", new[] { "Horizontal position", "Posición horizontal", "Horizontale Position", "Posição horizontal" } },
            { "Position verticale", new[] { "Vertical position", "Posición vertical", "Vertikale Position", "Posição vertical" } },
            { "En bas de l'écran en vue capot / intérieure", new[] { "At the bottom of the screen in hood / interior view", "Abajo de la pantalla en vista capó / interior", "Unten am Bildschirm in Motorhauben- / Innenansicht", "Na parte de baixo da tela na visão capô / interna" } },
            { "Autres informations (temps, position, tours)", new[] { "Other information (time, position, laps)", "Otra información (tiempo, posición, vueltas)", "Weitere Infos (Zeit, Position, Runden)", "Outras informações (tempo, posição, voltas)" } },
            { "Écartement horizontal", new[] { "Horizontal spread", "Separación horizontal", "Horizontaler Abstand", "Afastamento horizontal" } },
            { "Écartement vertical", new[] { "Vertical spread", "Separación vertical", "Vertikaler Abstand", "Afastamento vertical" } },
            { "Marge aux bords", new[] { "Edge margin", "Margen a los bordes", "Randabstand", "Margem das bordas" } },
            { "Écartement à 1 : blocs plaqués contre les bords de l'écran (utile en 16:9 / 21:9). Hauteur positive = plus haut.", new[] { "Spread at 1: blocks pushed against the screen edges (useful in 16:9 / 21:9). Positive height = higher.", "Separación a 1: bloques pegados a los bordes de la pantalla (útil en 16:9 / 21:9). Altura positiva = más arriba.", "Abstand 1: Blöcke an den Bildschirmrand gedrückt (nützlich bei 16:9 / 21:9). Positive Höhe = höher.", "Afastamento em 1: blocos encostados nas bordas da tela (útil em 16:9 / 21:9). Altura positiva = mais alto." } },
            { "Bezel (cadre de la borne d'arcade)", new[] { "Bezel (arcade cabinet frame)", "Bezel (marco de la máquina recreativa)", "Bezel (Rahmen des Arcade-Automaten)", "Bezel (moldura do fliperama)" } },
            { "Effet écran CRT (courbure, lignes)", new[] { "CRT screen effect (curvature, lines)", "Efecto pantalla CRT (curvatura, líneas)", "CRT-Bildschirmeffekt (Krümmung, Linien)", "Efeito de tela CRT (curvatura, linhas)" } },
            { "Contours noirs (cel shading)", new[] { "Black outlines (cel shading)", "Contornos negros (cel shading)", "Schwarze Konturen (Cel-Shading)", "Contornos pretos (cel shading)" } },
            { "Post-traitement BD (trame, couleurs)", new[] { "Comic post-processing (halftone, colours)", "Posprocesado cómic (trama, colores)", "Comic-Nachbearbeitung (Raster, Farben)", "Pós-processamento HQ (retícula, cores)" } },
            { "Trame BD sur les éclairages (halftone)", new[] { "Comic halftone on the lights", "Trama cómic en las luces (halftone)", "Comic-Raster auf den Lichtern (Halftone)", "Retícula HQ nas luzes (halftone)" } },
            { "Les changements sont appliqués immédiatement et enregistrés à la fermeture du menu.", new[] { "Changes are applied immediately and saved when the menu is closed.", "Los cambios se aplican al instante y se guardan al cerrar el menú.", "Änderungen werden sofort übernommen und beim Schließen des Menüs gespeichert.", "As alterações são aplicadas na hora e salvas ao fechar o menu." } },
            { "Aucun volant / contrôleur détecté. Branchez-le puis rouvrez ce menu.", new[] { "No wheel / controller detected. Plug it in, then reopen this menu.", "No se detecta ningún volante / controlador. Conéctalo y vuelve a abrir este menú.", "Kein Lenkrad / Controller erkannt. Anschließen und dieses Menü erneut öffnen.", "Nenhum volante / controle detectado. Conecte-o e reabra este menu." } },
            { "Périphérique", new[] { "Device", "Dispositivo", "Gerät", "Dispositivo" } },
            { "Configuré pour ce périphérique.", new[] { "Configured for this device.", "Configurado para este dispositivo.", "Für dieses Gerät eingerichtet.", "Configurado para este dispositivo." } },
            { "Pas encore configuré : lancez l'assistant.", new[] { "Not configured yet: run the wizard.", "Aún sin configurar: inicia el asistente.", "Noch nicht eingerichtet: Assistent starten.", "Ainda não configurado: inicie o assistente." } },
            { "Relancer l'assistant de configuration", new[] { "Run the setup wizard again", "Volver a iniciar el asistente", "Einrichtungsassistent erneut starten", "Reiniciar o assistente de configuração" } },
            { "Lancer l'assistant de configuration", new[] { "Run the setup wizard", "Iniciar el asistente de configuración", "Einrichtungsassistent starten", "Iniciar o assistente de configuração" } },
            { "Axes en direct", new[] { "Live axes", "Ejes en directo", "Achsen live", "Eixos ao vivo" } },
            { " (direction)", new[] { " (steering)", " (dirección)", " (Lenkung)", " (direção)" } },
            { " (accélérateur)", new[] { " (throttle)", " (acelerador)", " (Gas)", " (acelerador)" } },
            { " (frein)", new[] { " (brake)", " (freno)", " (Bremse)", " (freio)" } },
            { "Réglages", new[] { "Settings", "Ajustes", "Einstellungen", "Ajustes" } },
            { "Sensibilité de la direction", new[] { "Steering sensitivity", "Sensibilidad de la dirección", "Lenkempfindlichkeit", "Sensibilidade da direção" } },
            { "Zone morte des pédales", new[] { "Pedal dead zone", "Zona muerta de los pedales", "Pedal-Totzone", "Zona morta dos pedais" } },
            { "Sensibilité 1 = braquage maximal à fond de volant ; 2 = à mi-course (utile pour les volants 900°).", new[] { "Sensitivity 1 = full lock at full wheel turn; 2 = at half turn (useful for 900° wheels).", "Sensibilidad 1 = giro máximo con el volante a tope; 2 = a mitad de recorrido (útil para volantes de 900°).", "Empfindlichkeit 1 = voller Einschlag bei vollem Lenkradeinschlag; 2 = bei halbem (nützlich für 900°-Lenkräder).", "Sensibilidade 1 = esterço máximo com o volante no fim; 2 = na metade (útil para volantes de 900°)." } },
            { "Inverser la direction", new[] { "Invert steering", "Invertir la dirección", "Lenkung umkehren", "Inverter a direção" } },
            { "Oublier ce volant", new[] { "Forget this wheel", "Olvidar este volante", "Dieses Lenkrad vergessen", "Esquecer este volante" } },
            { "Retour de force", new[] { "Force feedback", "Force feedback", "Force Feedback", "Force feedback" } },
            { "Activer le retour de force", new[] { "Enable force feedback", "Activar el force feedback", "Force Feedback aktivieren", "Ativar o force feedback" } },
            { "Force générale", new[] { "Overall strength", "Fuerza general", "Gesamtstärke", "Força geral" } },
            { "Rappel au centre", new[] { "Centring force", "Retorno al centro", "Zentrierkraft", "Retorno ao centro" } },
            { "Glisse et chocs", new[] { "Slides and impacts", "Derrapes y choques", "Rutschen und Stöße", "Derrapagens e batidas" } },
            { "Inverser le sens des forces", new[] { "Invert force direction", "Invertir el sentido de las fuerzas", "Kraftrichtung umkehren", "Inverter o sentido das forças" } },
            { "État : ", new[] { "Status: ", "Estado: ", "Status: ", "Estado: " } },
            { "Position de repos", new[] { "Rest position", "Posición de reposo", "Ruheposition", "Posição de repouso" } },
            { "Volant au centre, pieds levés des pédales, puis cliquez sur Valider.", new[] { "Wheel centred, feet off the pedals, then click Confirm.", "Volante centrado, pies fuera de los pedales y pulsa Validar.", "Lenkrad mittig, Füße von den Pedalen, dann auf Bestätigen klicken.", "Volante no centro, pés fora dos pedais, depois clique em Confirmar." } },
            { "Tournez le volant à fond vers la DROITE", new[] { "Turn the wheel fully to the RIGHT", "Gira el volante a tope a la DERECHA", "Lenkrad ganz nach RECHTS drehen", "Gire o volante todo para a DIREITA" } },
            { "Enfoncez l'ACCÉLÉRATEUR à fond", new[] { "Press the THROTTLE fully", "Pisa el ACELERADOR a fondo", "GAS voll durchtreten", "Pise o ACELERADOR até o fim" } },
            { "Enfoncez le FREIN à fond", new[] { "Press the BRAKE fully", "Pisa el FRENO a fondo", "BREMSE voll durchtreten", "Pise o FREIO até o fim" } },
            { "Maintenez la position, puis cliquez sur Valider.", new[] { "Hold the position, then click Confirm.", "Mantén la posición y pulsa Validar.", "Position halten, dann auf Bestätigen klicken.", "Mantenha a posição e clique em Confirmar." } },
            { "   Détecté : ", new[] { "   Detected: ", "   Detectado: ", "   Erkannt: ", "   Detectado: " } },
            { "   (en attente d'un mouvement…)", new[] { "   (waiting for a movement…)", "   (esperando un movimiento…)", "   (warte auf eine Bewegung…)", "   (aguardando um movimento…)" } },
            { "Bouton « changer de vue »", new[] { "\"Change view\" button", "Botón \"cambiar de vista\"", "Taste „Ansicht wechseln“", "Botão \"mudar a visão\"" } },
            { "Bouton « menu des options »", new[] { "\"Options menu\" button", "Botón \"menú de opciones\"", "Taste „Optionsmenü“", "Botão \"menu de opções\"" } },
            { "Appuyez sur le bouton du volant à utiliser, ou cliquez sur Passer.", new[] { "Press the wheel button to use, or click Skip.", "Pulsa el botón del volante que quieras usar, o pulsa Omitir.", "Gewünschte Lenkradtaste drücken oder auf Überspringen klicken.", "Aperte o botão do volante a usar, ou clique em Pular." } },
            { "Valider", new[] { "Confirm", "Validar", "Bestätigen", "Confirmar" } },
            { "Passer", new[] { "Skip", "Omitir", "Überspringen", "Pular" } },
            { "Annuler", new[] { "Cancel", "Cancelar", "Abbrechen", "Cancelar" } },
            { "Assigné : ", new[] { "Assigned: ", "Asignado: ", "Zugewiesen: ", "Atribuído: " } },
            { "Annulé", new[] { "Cancelled", "Cancelado", "Abgebrochen", "Cancelado" } },
            { "Système de contrôles (Rewired) non prêt.", new[] { "Control system (Rewired) not ready.", "Sistema de controles (Rewired) no disponible.", "Steuerungssystem (Rewired) nicht bereit.", "Sistema de controles (Rewired) não está pronto." } },
            { "Clavier", new[] { "Keyboard", "Teclado", "Tastatur", "Teclado" } },
            { "Manette", new[] { "Gamepad", "Mando", "Gamepad", "Controle" } },
            { "Volant / pédalier", new[] { "Wheel / pedals", "Volante / pedales", "Lenkrad / Pedale", "Volante / pedais" } },
            { "Aucune manette détectée.", new[] { "No gamepad detected.", "No se detecta ningún mando.", "Kein Gamepad erkannt.", "Nenhum controle detectado." } },
            { "Appuyez sur la touche / le bouton à assigner… (6 s)", new[] { "Press the key / button to assign… (6 s)", "Pulsa la tecla / el botón que quieras asignar… (6 s)", "Taste / Knopf zum Zuweisen drücken… (6 s)", "Aperte a tecla / o botão a atribuir… (6 s)" } },
            { "Aucune configuration pour ce périphérique.", new[] { "No configuration for this device.", "No hay configuración para este dispositivo.", "Keine Konfiguration für dieses Gerät.", "Nenhuma configuração para este dispositivo." } },
            { "Carte ", new[] { "Map ", "Mapa ", "Belegung ", "Mapa " } },
            { "Rétablir les contrôles par défaut", new[] { "Restore default controls", "Restablecer los controles por defecto", "Standardsteuerung wiederherstellen", "Restaurar os controles padrão" } },
            { "Contrôles par défaut rétablis", new[] { "Default controls restored", "Controles por defecto restablecidos", "Standardsteuerung wiederhergestellt", "Controles padrão restaurados" } },
            { "Cliquez sur une touche pour la changer. Les contrôles sont partagés avec Takeover 2.", new[] { "Click a key to change it. Controls are shared with Takeover 2.", "Haz clic en una tecla para cambiarla. Los controles se comparten con Takeover 2.", "Auf eine Taste klicken, um sie zu ändern. Die Steuerung wird mit Takeover 2 geteilt.", "Clique numa tecla para mudá-la. Os controles são compartilhados com Takeover 2." } },
            { "Impossible de démarrer l'assignation", new[] { "Could not start the assignment", "No se pudo iniciar la asignación", "Zuweisung konnte nicht gestartet werden", "Não foi possível iniciar a atribuição" } },
            { "désactivé", new[] { "disabled", "desactivado", "deaktiviert", "desativado" } },
            { "erreur : ", new[] { "error: ", "error: ", "Fehler: ", "erro: " } },
            { "aucun volant à retour de force détecté", new[] { "no force feedback wheel detected", "no se detecta ningún volante con force feedback", "kein Lenkrad mit Force Feedback erkannt", "nenhum volante com force feedback detectado" } },
            { "actif sur ", new[] { "active on ", "activo en ", "aktiv auf ", "ativo em " } },
            { "Poursuite (origine)", new[] { "Chase (original)", "Persecución (original)", "Verfolger (Original)", "Perseguição (original)" } },
            { "Capot", new[] { "Hood", "Capó", "Motorhaube", "Capô" } },
            { "Intérieure sans capot", new[] { "Interior without hood", "Interior sin capó", "Innen ohne Motorhaube", "Interna sem capô" } },
            { "Temps restant", new[] { "Time left", "Tiempo restante", "Verbleibende Zeit", "Tempo restante" } },
            { "Position", new[] { "Position", "Posición", "Position", "Posição" } },
            { "Tour", new[] { "Lap", "Vuelta", "Runde", "Volta" } },
            { "Temps au tour", new[] { "Lap times", "Tiempos por vuelta", "Rundenzeiten", "Tempos de volta" } },
            { "Messages (départ, checkpoint, contresens…)", new[] { "Messages (start, checkpoint, wrong way…)", "Mensajes (salida, checkpoint, sentido contrario…)", "Meldungen (Start, Checkpoint, falsche Richtung…)", "Mensagens (largada, checkpoint, contramão…)" } },
            { "Langue", new[] { "Language", "Idioma", "Sprache", "Idioma" } },
            { "Présentation de la voiture avant le compte à rebours", new[] { "Car presentation before the countdown", "Presentación del coche antes de la cuenta atrás", "Fahrzeugvorstellung vor dem Countdown", "Apresentação do carro antes da contagem regressiva" } },
            { "Audio", new[] { "Audio", "Audio", "Audio", "Áudio" } },
            { "Volumes", new[] { "Volume", "Volumen", "Lautstärke", "Volume" } },
            { "Général", new[] { "Master", "General", "Gesamt", "Geral" } },
            { "Musique", new[] { "Music", "Música", "Musik", "Música" } },
            { "Moteurs", new[] { "Engines", "Motores", "Motoren", "Motores" } },
            { "Effets sonores", new[] { "Sound effects", "Efectos de sonido", "Soundeffekte", "Efeitos sonoros" } },
            { "Bande-son", new[] { "Soundtrack", "Banda sonora", "Soundtrack", "Trilha sonora" } },
            { "Originale (90's GP)", new[] { "Original (90's GP)", "Original (90's GP)", "Original (90's GP)", "Original (90's GP)" } },
            { "Aléatoire (vos musiques)", new[] { "Random (your music)", "Aleatoria (tu música)", "Zufällig (eigene Musik)", "Aleatória (suas músicas)" } },
            { "Vos propres musiques (mp3, ogg, wav) : placez-les dans le dossier « Music » à côté de 9OsGP.exe.", new[] { "Your own music (mp3, ogg, wav): put it in the \"Music\" folder next to 9OsGP.exe.", "Tu propia música (mp3, ogg, wav): colócala en la carpeta \"Music\" junto a 9OsGP.exe.", "Eigene Musik (mp3, ogg, wav): in den Ordner „Music“ neben 9OsGP.exe legen.", "Suas próprias músicas (mp3, ogg, wav): coloque-as na pasta \"Music\" ao lado de 9OsGP.exe." } },
            { "Quitter le jeu ?", new[] { "Quit the game?", "¿Salir del juego?", "Spiel beenden?", "Sair do jogo?" } },
            { "Quitter", new[] { "Quit", "Salir", "Beenden", "Sair" } },
            { "Entrée : quitter   —   Échap : annuler", new[] { "Enter: quit   —   Esc: cancel", "Intro: salir   —   Esc: cancelar", "Eingabe: beenden   —   Esc: abbrechen", "Enter: sair   —   Esc: cancelar" } },
            { "Tournez le volant à fond vers la GAUCHE", new[] { "Turn the wheel fully to the LEFT", "Gira el volante a tope a la IZQUIERDA", "Lenkrad ganz nach LINKS drehen", "Gire o volante todo para a ESQUERDA" } },
            { "Pieds levés des pédales, puis cliquez sur Valider.", new[] { "Feet off the pedals, then click Confirm.", "Pies fuera de los pedales y pulsa Validar.", "Füße von den Pedalen, dann auf Bestätigen klicken.", "Pés fora dos pedais, depois clique em Confirmar." } },
            { "Bouton « START » (départ, pause)", new[] { "\"START\" button (start, pause)", "Botón \"START\" (salida, pausa)", "Taste „START“ (Start, Pause)", "Botão \"START\" (largada, pausa)" } },
            { "Bougez le volant et les pédales : seuls les axes utilisés s'affichent.", new[] { "Move the wheel and pedals: only the axes in use are shown.", "Mueve el volante y los pedales: solo se muestran los ejes usados.", "Lenkrad und Pedale bewegen: nur genutzte Achsen werden angezeigt.", "Mova o volante e os pedais: só os eixos usados aparecem." } },
            { "Recentrer : volant droit, puis cliquez ici", new[] { "Re-centre: wheel straight, then click here", "Recentrar: volante recto y haz clic aquí", "Zentrieren: Lenkrad gerade, dann hier klicken", "Recentralizar: volante reto, depois clique aqui" } },
            { "Sorties façon MAME (réseau, port 8000)", new[] { "MAME-style outputs (network, port 8000)", "Salidas estilo MAME (red, puerto 8000)", "Ausgänge wie MAME (Netzwerk, Port 8000)", "Saídas estilo MAME (rede, porta 8000)" } },
            { "Bouton Start qui clignote pendant l'attract, voyant de vue en course (MameHooker, LEDBlinky…).", new[] { "Start button blinking during attract, view lamp in race (MameHooker, LEDBlinky…).", "Botón Start parpadeando en modo demo, luz de vista en carrera (MameHooker, LEDBlinky…).", "Start-Taste blinkt im Attract-Modus, Ansichtslampe im Rennen (MameHooker, LEDBlinky…).", "Botão Start piscando no modo demo, luz de visão na corrida (MameHooker, LEDBlinky…)." } },
            { "100 % = volume d'origine. Au-delà, le son est amplifié (le moteur est discret dans le jeu d'origine).", new[] { "100 % = original volume. Above, the sound is amplified (the engine is quiet in the original game).", "100 % = volumen original. Por encima, el sonido se amplifica (el motor suena bajo en el juego original).", "100 % = Originallautstärke. Darüber wird verstärkt (der Motor ist im Originalspiel leise).", "100 % = volume original. Acima disso, o som é amplificado (o motor é baixo no jogo original)." } },
            { "Gameplay", new[] { "Gameplay", "Jugabilidad", "Gameplay", "Jogabilidade" } },
            { "Style de dérapage", new[] { "Drift style", "Estilo de derrape", "Drift-Stil", "Estilo de derrapagem" } },
            { "Origine", new[] { "Original", "Original", "Original", "Original" } },
            { "Dérapage d'origine du jeu : frein maintenu + braquage à fond au-dessus de 150 km/h.", new[] { "The game's original drift: hold the brake + full lock above 150 km/h.", "Derrape original del juego: freno pisado + giro a tope por encima de 150 km/h.", "Original-Drift des Spiels: Bremse halten + voller Einschlag über 150 km/h.", "Derrapagem original do jogo: freio pressionado + esterço total acima de 150 km/h." } },
            { "En virage, lâchez l'accélérateur et réappuyez aussitôt (ou un coup de frein) : glisse très stable qui garde la vitesse. Le volant règle la trajectoire ; lâcher les gaz ou redresser fait raccrocher.", new[] { "In a turn, lift off and floor it again (or tap the brake): a very stable slide that keeps its speed. The wheel sets the line; lifting off or straightening grips again.", "En curva, suelta el acelerador y vuelve a pisarlo (o un toque de freno): derrape muy estable que conserva la velocidad. El volante marca la trazada; soltar gas o enderezar recupera agarre.", "In der Kurve kurz vom Gas und wieder voll drauf (oder kurz bremsen): sehr stabiler Drift ohne Tempoverlust. Das Lenkrad bestimmt die Linie; Gas weg oder geradeaus lenken beendet ihn.", "Na curva, solte o acelerador e pise de novo (ou um toque no freio): derrapagem muito estável que mantém a velocidade. O volante define a trajetória; soltar o gás ou endireitar recupera a aderência." } },
            { "Même entrée qu'à Ridge Racer. Grande glisse très tolérante qui tient tant que le volant est braqué, même sans gaz ; un contre-braquage franc la bascule de l'autre côté pour enchaîner les S.", new[] { "Same entry as Ridge Racer. A huge, forgiving slide that holds as long as the wheel is turned, even off the throttle; a firm counter-steer flips it to the other side for S-bends.", "Misma entrada que Ridge Racer. Derrape amplio y tolerante que dura mientras el volante esté girado, incluso sin gas; un contravolante firme lo cambia de lado para enlazar eses.", "Gleicher Einstieg wie Ridge Racer. Großer, gutmütiger Drift, der hält, solange eingelenkt ist, auch ohne Gas; kräftiges Gegenlenken wechselt die Seite für S-Kurven.", "Mesma entrada do Ridge Racer. Derrapagem ampla e tolerante que dura enquanto o volante estiver virado, mesmo sem gás; um contra-esterço firme troca de lado para emendar os S." } },
            { "Coup de frein en braquant : l'arrière décroche vraiment et continue de tourner sous les gaz. Contre-braquez pour tenir l'angle, sinon c'est le tête-à-queue ; lâcher les gaz fait raccrocher. La glisse coûte de la vitesse.", new[] { "Tap the brake while turning: the tail really steps out and keeps rotating under power. Counter-steer to hold the angle or you spin; lifting off tucks it back in. Sliding costs speed.", "Toque de freno girando: la trasera se suelta de verdad y sigue girando con gas. Contravolantea para mantener el ángulo o trompo; soltar gas recupera agarre. Derrapar cuesta velocidad.", "Kurz bremsen beim Einlenken: das Heck bricht wirklich aus und dreht unter Gas weiter. Gegenlenken hält den Winkel, sonst Dreher; Gas weg fängt das Heck. Driften kostet Tempo.", "Toque no freio virando: a traseira escapa de verdade e continua girando com gás. Contra-esterce para segurar o ângulo, senão rodada; soltar o gás recupera a aderência. Derrapar custa velocidade." } },
            { "MEILLEURS TEMPS", new[] { "BEST TIMES", "MEJORES TIEMPOS", "BESTZEITEN", "MELHORES TEMPOS" } },
            { "NOM", new[] { "NAME", "NOMBRE", "NAME", "NOME" } },
            { "TEMPS", new[] { "TIME", "TIEMPO", "ZEIT", "TEMPO" } },
            { "MEILLEUR TOUR", new[] { "BEST LAP", "MEJOR VUELTA", "BESTE RUNDE", "MELHOR VOLTA" } },
            { "NOUVEAU RECORD !", new[] { "NEW RECORD!", "NUEVO RECORD!", "NEUER REKORD!", "NOVO RECORDE!" } },
            { "ENTREZ VOTRE NOM", new[] { "ENTER YOUR NAME", "INTRODUCE TU NOMBRE", "NAMEN EINGEBEN", "DIGITE SEU NOME" } },
        };
    }
}
