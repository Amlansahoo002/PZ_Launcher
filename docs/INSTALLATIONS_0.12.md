# Installations Steam, GOG et dossiers personnalisés — 0.12.0

**Réglages → Installation** permet de consulter le dossier actif, sélectionner une installation détectée ou saisir/parcourir un autre dossier. Le bouton **Utiliser ce chemin** valide puis enregistre le choix. Le bouton **Utiliser la sélection** applique une entrée du catalogue. **Détecter à nouveau** actualise le catalogue sans changer le dossier actif.

Le catalogue affiche l’origine, la version lisible dans le code du jeu et le chemin complet. Un `?` indique que la version n’a pas pu être déterminée. Les chemins restent copiables dans les champs et accessibles au survol de la liste.

## Détection et persistance

- **Steam :** répertoires du client déclarés dans le registre utilisateur/machine, vues 32 et 64 bits, puis `libraryfolders.vdf` dans `steamapps` et `config`. Les formats modernes et anciens sont lus. Toutes les bibliothèques déclarées sont examinées, quel que soit leur disque. `appmanifest_108600.acf` fournit le dossier `installdir` ; le dossier conventionnel `steamapps/common/ProjectZomboid` reste également reconnu.
- **GOG :** chemins déclarés sous `SOFTWARE/GOG.com/Games` et emplacements des programmes désinstallables publiés par GOG, dans les vues utilisateur/machine et 32/64 bits. Les emplacements conventionnels sont aussi examinés lorsqu’ils contiennent des métadonnées `goggame-*.info`. La reconnaissance des entrées de désinstallation et des métadonnées locales a été recoupée avec le [code source du module GOG de Playnite](https://github.com/JosefNemec/PlayniteExtensions/blob/master/source/Libraries/GogLibrary/GogLibrary.cs).
- **Installation déplacée ou portable :** choix manuel du dossier, même hors des emplacements déclarés par Steam/GOG. Les chemins déjà utilisés sont conservés dans `InstallationPaths`, avec un maximum de 20 entrées distinctes. Les guillemets, variables d’environnement et chemins vers `ProjectZomboid64.exe`, `ProjectZomboid64.json` ou `projectzomboid.jar` sont normalisés vers le dossier.
- **Disque déconnecté :** le chemin actif reste mémorisé et est signalé indisponible. Il n’est plus remplacé automatiquement par une autre installation. Reconnecter le disque ou sélectionner explicitement un autre dossier.

Les doublons sont éliminés sans distinction de casse. Un manifeste inaccessible ou incomplet n’empêche pas la lecture des autres bibliothèques. Le catalogue recherche les emplacements déclarés et mémorisés ; il ne parcourt pas récursivement tous les fichiers des disques.

## Validation et lancement

Le dossier doit contenir `ProjectZomboid64.json`, `jre64/bin/java.exe` et le code PZ : soit `projectzomboid.jar`, soit `zombie/gameStates/MainScreenState.class` à la racine ou sous `java`. La version est lue dans `Core.class`, en archive ou en fichier, sans exécuter le jeu. Ces contrôles vérifient les composants attendus, sans remplacer une vérification d’intégrité de l’installation.

Le client utilise toujours les arguments, le classpath et la classe principale du JSON de l’installation sélectionnée. Le Java et le répertoire de travail proviennent de ce même dossier. Le changement actualise la détection matérielle/Java et le catalogue des mods ; il conserve les dossiers de profils, sauvegardes et copies de mods.

Une installation GOG reconnue lance le client sans Steam. Les cases Steam sont désactivées pour cette installation, mais les préférences enregistrées sont conservées pour un retour à Steam. Le même choix est appliqué au mode Steam du serveur hébergé. Un favori exigeant Steam et le navigateur Steam demandent de sélectionner une installation Steam.

La prise en charge du dossier ne modifie pas les exigences des agents Java : le loader communautaire existant reste prévu pour Java 25 et le JAR PZ. La recette du serveur hébergé reste celle de B42 étudiée précédemment. La reconnaissance de classes non empaquetées ne constitue pas une recette de toutes les anciennes versions du jeu et de leurs mods.

## Vérifications

Les exécutables finaux passent **61 contrôles en autonome et 60 en légère**, sans échec. L’écart correspond au GIF local présent dans `dist`. **256 rendus internes** couvrent les quatre langues, les tailles 1280 × 800 et 1080 × 700 et les variantes d’installation, sans problème détecté par les contrôles de mise en page. Les vues Installation ont également été inspectées visuellement. Il s’agit de rendus WinForms `DrawToBitmap`, pas d’une recette interactive complète.

Les scénarios automatisés couvrent trois bibliothèques Steam simulées (dont un dossier personnalisé issu du manifeste), les deux formats VDF, GOG et un dossier manuel, les doublons, la persistance, le disque absent, les manifestes incomplets, le refus d’un `installdir` sortant de sa bibliothèque, le lancement GOG préparé sans Steam et le retour aux préférences Steam.

Un scénario teste des classes non empaquetées, extraites du JAR B42 installé, pour vérifier la lecture de version et la conservation d’un classpath différent. Un contrôle WinForms saisit puis applique le dossier GOG simulé et vérifie le chemin actif, le profil conservé et la réactivation des contrôles.

L’installation Steam locale est détectée et son plan de lancement est vérifié. **Aucune installation GOG réelle n’est présente sur la machine : la détection et les commandes GOG sont testées avec des fixtures, sans lancement du jeu GOG.** Les rendus GOG, disque absent et liste multiple utilisent également des données fictives.

Rapports de livraison : [contrôles autonome](../artifacts/release-0.12.0/verification.json), [contrôles légère](../artifacts/release-0.12.0-light/verification.json), [rendus](../artifacts/ui-review-0.12-published/rendering.json), [empreintes et bilan](../artifacts/release-0.12.0/delivery.json).
