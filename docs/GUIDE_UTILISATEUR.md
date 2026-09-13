# PZLauncher Community — Guide utilisateur

Version 0.21.0 · Windows x64

PZLauncher gère les installations de Project Zomboid, les profils de jeu, les mods, les sauvegardes et les serveurs dédiés. Le jeu doit déjà être installé. Le launcher utilise le Java fourni avec cette installation ; son exécutable autonome ne demande pas d'installation séparée de .NET.

Cette version vise principalement la Build 42. Certains mods, loaders Java ou packs exigent une version précise du jeu. PZLauncher est un projet communautaire sans affiliation avec The Indie Stone.

## Sommaire

- [Premier lancement](#guide-premier-lancement)
- [Installations et profils](#guide-installations-et-profils)
- [Mods du jeu et SteamCMD](#guide-mods-du-jeu-et-steamcmd)
- [Mods Java](#guide-mods-java)
- [Jeu en ligne](#guide-jeu-en-ligne)
- [Serveurs dédiés](#guide-serveurs-dedies)
- [Packs partagés serveur/client](#guide-packs-partages)
- [Sauvegardes et outils de monde](#guide-sauvegardes)
- [Réglages de performances](#guide-performances)
- [Résoudre un problème](#guide-depannage)
- [Mises à jour et emplacement des fichiers](#guide-fichiers)

<a id="guide-premier-lancement"></a>
## Premier lancement

1. Conservez les fichiers de la livraison ensemble dans un dossier accessible en écriture, puis ouvrez **PZLauncher.exe**.
2. Vérifiez l'installation du jeu dans **Réglages**. Si nécessaire, choisissez manuellement le dossier de Project Zomboid.
3. Sélectionnez le **Profil de jeu** en haut. Le bouton **+** permet de créer une configuration séparée.
4. Dans Réglages, vérifiez le mode Steam et laissez **JVM automatique** activé pour obtenir un point de départ adapté au PC.
5. Sélectionnez vos mods du jeu, puis cliquez sur **Enregistrer la sélection**. Les mods Java disposent de leurs propres réglages.
6. Cliquez sur **Jouer**, puis choisissez ou créez votre monde dans PZ.

Le sélecteur de langue du volet gauche change la langue du launcher : anglais, français, espagnol, russe, portugais du Brésil, allemand ou chinois simplifié. Réglages → Traductions communautaires ouvre le dossier des traductions JSON et crée un modèle. Ajoutez un pack, redémarrez, puis choisissez sa langue. Les textes manquants utilisent l’anglais. Le fichier LANGUAGE-PACKS.md fourni avec l’exécutable explique le format. La langue et les options graphiques du jeu se règlent séparément. La fenêtre se redimensionne par ses bords ou ses coins ; sa taille et son état maximisé sont conservés à la fermeture.

<a id="guide-installations-et-profils"></a>
## Installations et profils

**Réglages** détecte les bibliothèques Steam sur plusieurs disques, reconnaît les installations GOG et accepte un dossier choisi manuellement. Choisissez le dossier du jeu, et non celui d'un mod. Si le disque est débranché, reconnectez-le ou sélectionnez une autre installation disponible.

Une installation GOG fonctionne sans Steam. La recherche de serveurs publics Steam exige une installation Steam et le client Steam ouvert. Une connexion directe doit utiliser le même mode Steam que le serveur.

Chaque profil possède ses réglages, sa sélection de mods et son dossier de cache. Le bouton d'ouverture du cache, sous le sélecteur de profil, permet de retrouver `options.ini`, `mods`, `Saves` et les autres fichiers du profil. Vérifiez le profil sélectionné avant d'installer des mods ou de modifier un monde.

Le profil initial peut utiliser votre dossier existant `%UserProfile%\Zomboid`. Les nouveaux profils disposent de dossiers séparés. Leur création ne déplace pas vos anciennes sauvegardes. Le partage de configuration transmet des réglages et des identifiants de mods, mais pas les fichiers des mods, les runtimes Java ni les mondes.

<a id="guide-mods-du-jeu-et-steamcmd"></a>
## Mods du jeu et SteamCMD

### Choisir les mods

Dans **Mods → Mods du jeu**, recherchez un nom ou un identifiant, utilisez les filtres de source, cochez les mods souhaités, résolvez les dépendances signalées et enregistrez la sélection. Les commandes de vérification et d'ordre aident à préparer la liste.

Un double-clic sur un mod affiche ses copies détectées et la source utilisée. Une copie locale peut être présente même si une autre copie est prioritaire. **Détails du scan** explique les dossiers examinés et les problèmes rencontrés. Vérifiez la présence de `mod.info` et une structure compatible avec la version du jeu.

Les mondes existants peuvent conserver leur propre liste de mods. Pour en modifier un, utilisez l'éditeur de monde ou les commandes prévues par PZ ; changer la sélection du profil ne remplace pas nécessairement celle du monde.

### Télécharger ou mettre à jour le Workshop

1. Dans le profil concerné, ouvrez **Mods → Workshop · SteamCMD**.
2. Saisissez les identifiants Workshop ou les liens pris en charge. Ces identifiants numériques servent au téléchargement ; ils diffèrent des IDs internes utilisés dans `Mods=` sur un serveur.
3. Installez SteamCMD depuis cet onglet si nécessaire. Le téléchargement cible Project Zomboid, application **108600**.
4. Lancez l'opération et suivez sa progression. Si l'accès anonyme est refusé, utilisez la connexion SteamCMD et complétez les demandes Steam/Steam Guard.
5. Actualisez la liste des mods, vérifiez les fichiers installés et enregistrez la sélection.

Les fichiers sont installés dans le cache du profil sélectionné. Les copies gérées remplacées sont sauvegardées. Une mise à jour détectée n'est pas appliquée automatiquement. Les collections Workshop ne sont pas développées automatiquement : fournissez les identifiants de leurs éléments.

<a id="guide-mods-java"></a>
## Mods Java

Pour un joueur, ouvrez **Mods → Mods Java**. Pour un serveur dédié, sélectionnez le serveur puis ouvrez **Serveurs → Mods Java**.

1. Activez l'application des mods Java.
2. Choisissez le loader demandé par l'auteur du mod.
3. Ajoutez le JAR, ou activez le mod du jeu qui le contient. Consultez son rôle et les messages de disponibilité/dépendances.
4. Pour Leaf ou ZombieBuddy, utilisez **Télécharger / installer** afin de choisir une version GitHub, ou indiquez un runtime existant. Le loader PZLauncher est déjà fourni.
5. Vérifiez les mods Java, puis lancez normalement le jeu ou le serveur.

| Choix | Utilisation |
|---|---|
| PZLauncher · API 1 | Mods conçus pour l'API Java 25 du launcher, sur une version du jeu et un rôle déclarés compatibles. |
| Leaf | Mods conçus pour Leaf, avec ses bibliothèques installées et l'environnement adapté. |
| ZombieBuddy | Mods conçus pour ZombieBuddy. Activez son mod compagnon si nécessaire et répondez aux demandes d'approbation de ZombieBuddy. |
| no agent | JAR de patch direct sans agent. **La case d'application de la configuration Java doit rester activée** pour charger les JAR sélectionnés. |

Les choix sont propres à chaque profil ou serveur. Les JAR inclus dans un mod du jeu suivent l'activation de ce mod. **Rôle non déclaré** signifie que le launcher ne sait pas déterminer la compatibilité client/serveur ; consultez l'auteur. Un rôle explicitement incompatible est refusé.

Le serveur utilise son point d'entrée dédié. Choisir Leaf ou ZombieBuddy ne rend pas tous leurs mods compatibles serveur. La vérification contrôle les métadonnées et les prérequis pris en charge ; seul le loader intégré lance une vérification séparée des transformations. Le fonctionnement réel d'une combinaison dépend des mods et de la version de PZ.

<a id="guide-jeu-en-ligne"></a>
## Jeu en ligne

Dans **Jeu en ligne → Serveurs publics**, actualisez la recherche. Les serveurs apparaissent progressivement et l'opération peut être annulée. Filtrez le nom/l'adresse, les joueurs, les mods, la version ou le ping ; cliquez sur les en-têtes pour trier. Un double-clic ou **Détails** ouvre les informations du serveur.

Les listes publiques de mods peuvent être incomplètes. Une information inconnue ne signifie pas « vanilla ». **Statistiques** résume les serveurs trouvés et les mods les plus visibles ; ce classement n'est pas une mesure exhaustive de leur popularité.

### Conserver un favori indépendant

Enregistrez le serveur avec un profil séparé, ou créez un favori dans **Connexion / favoris**. Renseignez l'adresse, les ports de jeu et de requête ainsi que le mode Steam. Chaque favori conserve ses propres copies de mods pour éviter de mélanger plusieurs serveurs.

Dans **Mods du serveur**, vérifiez les IDs internes et les IDs Workshop. Vous pouvez importer les listes d'un INI, copier les mods déjà installés ou ouvrir SteamCMD pour ce favori. Consultez les mises à jour avant de les appliquer : une version actuelle sur le Workshop peut différer de celle conservée par le serveur.

**Rejoindre** démarre PZ avec le profil et l'adresse du favori. Le jeu gère l'identification du joueur et son personnage. Les mots de passe de connexion saisis dans le launcher restent limités à la session.

### Demander la liste complète

Dans les détails, **Grab all mods infos** tente une connexion avec le compte fourni. L'opération s'arrête après réception des métadonnées, avant chargement d'un personnage ou téléchargement de mods. Le serveur peut enregistrer cette tentative et créer le compte si les inscriptions sont ouvertes. La version du jeu doit correspondre ; le composant actuel de capture cible 42.20.x.

La liste capturée est un instantané daté : actualisez-la après les changements du serveur. Elle ne prouve pas que vos patchs Java correspondent aux siens.

<a id="guide-serveurs-dedies"></a>
## Serveurs dédiés

1. Dans **Serveurs → Nouveau serveur**, créez et sélectionnez le serveur. Un cache indépendant lui est attribué.
2. Dans **Fichiers de configuration**, définissez le nom public, l'accès, les mots de passe, les ports, le nombre de joueurs et les options sandbox. Utilisez les formulaires disponibles ; l'édition du texte reste proposée pour les valeurs qu'ils ne représentent pas.
3. Configurez les mods du jeu et leurs identifiants Workshop, puis les **Mods Java** si nécessaire.
4. Dans **Lancement**, choisissez le mode Steam et la mémoire. Au premier démarrage, renseignez le mot de passe administrateur demandé pour la base du serveur.
5. Démarrez et attendez `SERVER STARTED` dans **Console serveur**. Utilisez cette console pour les commandes, la sauvegarde et l'arrêt propre. Gardez le launcher ouvert pendant qu'il gère le serveur.

Même en local, le joueur et le serveur utilisent deux processus et deux caches. Dans **Profils clients**, créez ou sélectionnez un client et indiquez `127.0.0.1` comme adresse ; le port est lu dans la configuration serveur. Démarrez le serveur avant **Rejoindre**. Un profil créé sans pack nécessite encore l'installation et la configuration des mods requis.

**Lancement → Exporter .bat / .sh** prépare un dossier avec le format Windows, Linux ou les deux. Conservez les scripts avec leur dossier `.assets`. Le BAT utilise l’installation et le cache Windows sélectionnés. Pour Linux, indiquez le cache cible puis copiez le SH et `.assets` dans la racine d’une installation Linux de la même version du jeu. Les dépendances Java sont incluses ; les fichiers de configuration, sauvegardes et mods classiques sont à transférer séparément. Le mot de passe administrateur est exclu sauf choix explicite. Le README du dossier décrit le lancement.

Pour les autres machines, utilisez une adresse permettant d'atteindre le serveur et configurez son accès réseau. Le launcher ne configure pas le routeur. Les serveurs déjà détectés conservent leur chemin d'origine : vérifiez-le avant modification.

<a id="guide-packs-partages"></a>
## Packs partagés serveur/client

Un pack rassemble des mods du jeu et des patchs Java directs, avec un manifeste `pz-runtime-pack.json`. Conservez ce manifeste avec les dossiers et fichiers fournis.

1. Choisissez **Serveurs → Créer depuis un pack** et sélectionnez le manifeste.
2. Dans **Profils clients**, créez un profil client. Le même pack est installé dans un autre cache, avec les patchs destinés au client. Les patchs exclusivement client restent hors du lancement serveur, et inversement.
3. Vérifiez la compatibilité des copies locales. **Associer un profil** accepte un profil existant déjà compatible ; cette commande ne remplace pas ses fichiers à votre place.
4. Démarrez le serveur, sélectionnez le client et rejoignez-le.

Un pack peut exiger le JAR exact du jeu, même si deux installations affichent le même numéro de version. Un fichier absent, modifié ou un conflit entre patchs doit être résolu avant lancement. Les loaders externes et les JAR ajoutés hors pack restent des réglages distincts.

### Remplacer le pack et revenir en arrière

Arrêtez le serveur et tous ses clients liés. Dans **Gérer le pack → Remplacer**, choisissez un manifeste différent du même pack, compatible avec l'installation actuelle du jeu.

Le launcher sauvegarde le groupe, prépare de nouveaux caches, les vérifie puis bascule les références ensemble. Un échec de préparation conserve le groupe précédent. Les anciens caches et sauvegardes restent disponibles et occupent de l'espace disque.

**Retour à la version précédente** restaure le pack **et l'état des mondes sauvegardé avant le remplacement**. La progression postérieure ne fait donc pas partie du monde restauré ; l'état courant est sauvegardé séparément avant le retour. Modifier la liste des clients liés après la mise à jour peut bloquer ce retour groupé tant que le groupe n'est plus cohérent.

Ces contrôles portent sur les fichiers locaux. Le launcher ne négocie pas encore l'empreinte du pack Java avec un serveur distant à la connexion. Une vérification réussie ne garantit pas le comportement multijoueur des mods.

<a id="guide-sauvegardes"></a>
## Sauvegardes et outils de monde

Choisissez le bon profil, puis le monde dans **Sauvegardes → Mondes**. Arrêtez le jeu ou serveur qui l'utilise avant toute modification.

- **Sauvegarder / restaurer :** créez une sauvegarde du monde et restaurez une copie séparée si nécessaire.
- **Éditer le monde :** consultez sa liste de mods et les champs pris en charge de `mods.txt`, `map_ver.bin`, `map_t.bin` et `map_sand.bin`. Tous les champs binaires possibles ne sont pas exposés.
- **Supprimer :** vérifiez le monde choisi avant de confirmer l'envoi à la corbeille Windows.
- **Chunk Wipe :** choisissez la zone et les types de données, prévisualisez les fichiers concernés puis appliquez la remise à zéro souhaitée. Les constructions des joueurs dans les chunks concernés peuvent être supprimées. Les commandes de sauvegarde et de retour arrière sont disponibles.
- **Base de données :** consultez les tables et champs décodés pris en charge. Il s'agit d'un lecteur, pas d'un éditeur général de base de données.

Le visualiseur accepte une image de fond, y compris un GIF. Si votre livraison contient `world.gif`, vous pouvez le choisir manuellement comme fond. Vérifiez son échelle et son alignement avant de sélectionner une zone à réinitialiser : l'image ne définit pas à elle seule les coordonnées du monde. La carte peut être détachée dans une fenêtre séparée.

### Monde distant par FTP ou FTPS

1. Dans **Sauvegardes → FTP / FTPS**, saisissez hôte, port, identifiant, mot de passe et protocole. **Connexion / lire le dossier** affiche les fichiers ; un double-clic ouvre un sous-dossier. Sélectionnez le dossier du monde, contenant notamment `map_sand.bin` ou `map`.
2. Arrêtez proprement le serveur et confirmez son arrêt. FTP ne peut pas contrôler le processus distant. Gardez-le arrêté jusqu’à la fin des remplacements.
3. Décochez **Inclure les chunks** pour ne rapatrier que les fichiers de l’éditeur et les bases SQLite. Pour ChunkWiper, laissez la case cochée : les dossiers de chunks pris en charge seront rapatriés aussi, ce qui peut représenter un transfert important.
4. Modifiez la copie via **Éditer le monde** ou prévisualisez/appliquez le **Chunk Wipe**. Ces opérations portent sur la copie locale.
5. Cliquez **Vérifier / envoyer les changements** et relisez les remplacements/suppressions. Seuls les fichiers rapatriés dont le contenu a changé, ou qui ont été retirés par le wipe, sont concernés. Un fichier distant modifié entre-temps bloque l’envoi. Les originaux restent sur le serveur avec un suffixe `.pzlauncher-*.bak` ; le rapport local contient les chemins de récupération.

Les copies se trouvent dans `%LocalAppData%\PZLauncher\ftp-workspaces`. **Reprendre une copie** ouvre son `manifest.json`, recharge la connexion sans mot de passe et permet de reprendre les modifications non envoyées. Saisissez à nouveau le mot de passe. Les journaux SQLite actifs, locaux ou distants, doivent être consolidés avant transfert ; fermez les éditeurs de base après modification. FTP/FTPS assure les transferts de fichiers ; les commandes SSH/SFTP ne font pas partie de ce parcours.

<a id="guide-performances"></a>
## Réglages de performances

La **JVM automatique** adapte la mémoire de départ et le collecteur au PC et à la RAM disponible avant lancement, en conservant de la place pour Windows et les autres allocations. Donner davantage de mémoire n'améliore pas systématiquement les performances.

Pour des valeurs manuelles, désactivez ce mode, modifiez les réglages puis enregistrez. Les options de pause/déduplication G1 ne s'appliquent pas sous ZGC. Les serveurs disposent de leurs propres réglages ; joueurs, carte et mods influencent leurs besoins.

Le mode normal fournit ses arguments de lancement sans imposer de modifier les BAT ou JSON du jeu. L'éditeur d'arguments avancés et l'aperçu de commande permettent de reproduire une configuration particulière. Évitez d'y répéter la mémoire, le collecteur ou Steam : leurs champs dédiés les gèrent déjà. Aucun profil ne garantit un gain de FPS.

<a id="guide-depannage"></a>
## Résoudre un problème

| Problème | À vérifier |
|---|---|
| Installation introuvable | Dossier choisi dans Réglages, disque connecté, fichiers du jeu et Java fourni présents. |
| Mod local absent | Profil, filtre, structure de `mod.info` et détails du scan. Un double-clic sur un mod détecté affiche ses copies. |
| Activation refusée | Message de disponibilité, version ciblée et dépendances nécessaires. |
| Échec Java | Loader choisi, chemin du runtime, rôle et build. Un patch direct reste en no agent sauf indication différente de son auteur. |
| Pack incompatible | Installation sélectionnée et fichiers originaux du pack. Obtenez le pack correspondant plutôt que de modifier son empreinte. |
| Aucun serveur public | Steam ouvert, installation Steam choisie, actualisation et message réseau. Le favori saisi manuellement utilise un autre parcours. |
| Connexion impossible | Adresse, port de jeu, Steam, version, compte/mots de passe et versions des mods. Démarrez d'abord le serveur local. |
| Modification bloquée | Jeu ou serveur arrêté pour ce cache. Actualisez après une modification externe afin de ne pas écraser des changements plus récents. |
| Fermeture ou comportement inattendu | Ouvrez Diagnostic, sélectionnez le journal concerné et un marqueur de problème pour lire les lignes originales. Le dédié possède aussi sa console. |

Pour signaler un problème, indiquez les versions du launcher et du jeu, le loader, l'opération effectuée et les lignes utiles du diagnostic. Relisez les journaux avant partage : ils peuvent contenir des chemins locaux, adresses et informations de compte. Un mod mentionné dans une trace n'est pas nécessairement responsable.

<a id="guide-fichiers"></a>
## Mises à jour et emplacement des fichiers

Fermez le launcher avant de remplacer ses fichiers par une nouvelle livraison. Il ne met pas encore son propre exécutable à jour automatiquement. Cette opération est distincte des mises à jour du jeu, d'un loader ou d'un pack.

Les réglages du launcher se trouvent normalement dans `%LocalAppData%\PZLauncher`. Les réglages du jeu, mods et sauvegardes se trouvent dans les caches des profils/serveurs : utilisez leurs boutons d'ouverture de dossier. Remplacer l'EXE ne déplace pas ces caches. Pour sauvegarder ou déplacer toute votre configuration, conservez les réglages du launcher et les caches nécessaires.

La section **Community / À propos** présente les crédits, les liens des projets et le Discord officiel de Project Zomboid. L'emplacement GitHub du launcher reste réservé jusqu'à configuration d'un dépôt public.
