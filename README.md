# PixelLiveMod

Plugin C# para **10 Million Pixels Demo** com BepInEx 5, acompanhado de um conector Node.js/TypeScript para presentes de lives públicas do TikTok. O plugin também oferece hotkeys locais para criar bolas. O jogo, seus assets e as DLLs do BepInEx não fazem parte deste repositório.

## Arquitetura

TikTok LIVE (presentes) → conector Node.js → JSON por TCP em `127.0.0.1:19081` → plugin Unity → fila de spawn.

O conector identifica presentes pelo catálogo da sala (nome/ID e custo em moedas), trata sequências e envia comandos de 1 a 20 bolas. O plugin aceita comandos na thread principal do Unity, coloca as bolas na fila e responde se **enfileirou** o pedido. A resposta não confirma a criação de todas as bolas; o resultado aparece em `BepInEx/LogOutput.log` e alimenta o feed após a conclusão. A ponte escuta somente na interface local.

O plugin usa C# e .NET Standard 2.1 com BepInEx 5.4.23.5 e assemblies Unity da cópia local do jogo. O conector usa Node.js 20+, TypeScript e `tiktok-live-connector` 2.4.0.

## Arquivos principais

- `PixelLiveMod.cs`, `BallSpawnService.cs` e `LocalSpawnBridge.cs`: plugin, fila de bolas e ponte TCP.
- `InfernoBall.cs`, `InfernoExplosion.cs`, `InfernoExplosionVFX.cs` e `InfernoSettings.cs`: implementação da Inferno Ball. Consulte [INFERNO.md](INFERNO.md).
- `tiktok-connector/src/`: conexão TikTok, identificação de presentes e cliente TCP.
- `tiktok-connector/src/tests/`: testes automatizados do conector.
- `.env.example`: modelo das configurações locais do TikTok e da porta da ponte.
- `tiktok-connector/config.example.json`: configuração segura de exemplo com os nove presentes.
- `Start-Isolated-Game.cmd`: inicia uma cópia local com o plugin instalado.

## Pré-requisitos e instalação do jogo

### Instalação automática no Windows

Execute `Setup-PixelLiveMod.cmd` na raiz do repositório e informe a pasta que contém `10 Million Pixels Demo.exe` quando solicitado. O script usa uma cópia isolada em `%TEMP%\PixelLiveMod-10MillionPixels-Isolated`; não altera a instalação original. Ele obtém .NET SDK e Node.js LTS localmente se faltarem, baixa o BepInEx 5.4.23.5 do release oficial, instala os pacotes npm, compila o plugin e o conector, copia a DLL para `BepInEx/plugins` e abre o jogo modificado. O jogo deve estar fechado durante a instalação. O jogo em si não é distribuído pelo repositório e precisa estar instalado legitimamente no PC.

O instalador cria `.env` e `tiktok-connector/config.json` a partir dos exemplos somente se esses arquivos ainda não existirem. Para ativar a conexão TikTok, configure `TIKTOK_USERNAME` em `.env` e execute `Start-TikTok-Connector.cmd` na raiz do repositório; esse iniciador também encontra o Node.js instalado localmente pelo setup. Para preparar sem abrir o jogo, use `powershell -ExecutionPolicy Bypass -File .\Setup-PixelLiveMod.ps1 -GamePath 'C:\caminho\do\jogo' -NoLaunch`. O parâmetro `-DryRun` valida os caminhos sem baixar nem alterar arquivos.

### Instalação manual

1. No Windows, tenha uma cópia legítima de **10 Million Pixels Demo** (Unity Mono), o .NET SDK capaz de compilar `netstandard2.1` e Node.js 20+ com npm.
2. Prepare uma cópia local do jogo fora deste repositório. O caminho padrão usado pelo projeto é `%TEMP%\PixelLiveMod-10MillionPixels-Isolated`. Manter a cópia separada evita alterar os arquivos da instalação Steam.
3. Instale o BepInEx **5.4.23.5**, na distribuição compatível com a arquitetura do jogo: extraia os arquivos na raiz dessa cópia, junto de `10 Million Pixels Demo.exe`, e execute o jogo uma vez para gerar `BepInEx/config` e `BepInEx/plugins`.
4. Confirme que existem `BepInEx/core/BepInEx.dll` e `10 Million Pixels Demo_Data/Managed/Assembly-CSharp.dll` nessa cópia. As outras DLLs Unity referenciadas em `PixelLiveMod.csproj` ficam no mesmo diretório `Managed`. Elas são dependências locais e não devem ser copiadas para o Git.

Se a cópia estiver em outro diretório, defina `PIXELLIVEMOD_GAME_DIR` no terminal antes de compilar e iniciar o jogo. O projeto aceita também `dotnet build -c Release -p:GameCopy="C:\caminho\da\copia"`. A propriedade `GameCopy` tem prioridade sobre a variável de ambiente; sem ambas, o projeto usa o caminho padrão em `%TEMP%`.

```powershell
$env:PIXELLIVEMOD_GAME_DIR = 'C:\caminho\da\copia'
dotnet build .\PixelLiveMod.csproj -c Release
Copy-Item .\bin\Release\netstandard2.1\PixelLiveMod.dll "$env:PIXELLIVEMOD_GAME_DIR\BepInEx\plugins\PixelLiveMod.dll"
```

Para a cópia no caminho padrão, use esse diretório como destino do `Copy-Item`. O build não instala o plugin automaticamente. Feche o jogo antes de substituir a DLL.

## Conector TikTok LIVE

```powershell
Copy-Item .\.env.example .\.env
# Edite TIKTOK_USERNAME em .env para a conta da live.
cd .\tiktok-connector
Copy-Item .\config.example.json .\config.json
npm ci
npm run build
npm test
npm start
```

Antes de `npm start`, edite `TIKTOK_USERNAME` no `.env` da raiz para o nome de uma conta com live pública ativa, com ou sem `@`. `PIXELLIVEMOD_BRIDGE_PORT` no mesmo arquivo deve ser igual a `[Bridge] Port` em `BepInEx/config/com.pixellive.mod.cfg` (padrão `19081`). O conector carrega o `.env` automaticamente; variáveis já definidas no ambiente têm prioridade. `.env` e `tiktok-connector/config.json` são locais e ignorados pelo Git. Reinicie o conector após mudar a configuração. Ele pode iniciar antes do jogo e tenta reconectar à ponte local. Para detalhes sobre identificação de presentes e limites da conexão, consulte [tiktok-connector/README.md](tiktok-connector/README.md).

Na raiz do projeto, execute `Start-Isolated-Game.cmd` para abrir a cópia com o plugin. Se estiver usando um diretório diferente do padrão, execute o script no mesmo terminal em que definiu `PIXELLIVEMOD_GAME_DIR`. Verifique no `BepInEx/LogOutput.log` as linhas de carregamento do PixelLiveMod e da ponte.

### Mapeamento de presentes

As quantidades abaixo são **bolas por presente**, configuráveis de 1 a 20 em `config.json`. `coins` identifica o presente; não determina a quantidade de bolas. Os nomes são comparados exatamente. O exemplo não contém IDs de presentes não confirmados.

| Presente | Moedas | Bola | Quantidade |
|---|---:|---|---:|
| Rose | 1 | Orange | 1 |
| Finger Heart | 5 | Orange | 5 |
| TikTok | 1 | Yellow | 1 |
| Perfume | 20 | Yellow | 5 |
| GG | 1 | Green | 1 |
| Rosa | 10 | Green | 5 |
| Heart Me | 1 | Pink | 1 |
| Doughnut | 30 | Pink | 5 |
| Hand Heart | 100 | Inferno | 1 |

## Hotkeys locais

As hotkeys usam a posição atual do mouse no mundo do jogo. As quantidades ficam em `[Hotkeys]` de `BepInEx/config/com.pixellive.mod.cfg` e aceitam inteiros de 1 a 20. Edite o arquivo com o jogo fechado e reinicie.

| Tecla | Chave no arquivo | Bola | Padrão |
|---|---|---|---:|
| F5 | `F5_Orange_Quantity` | Orange | 1 |
| Shift+F5 | `Shift_F5_Orange_Quantity` | Orange | 5 |
| F6 | `F6_Yellow_Quantity` | Yellow | 1 |
| Shift+F6 | `Shift_F6_Yellow_Quantity` | Yellow | 5 |
| F7 | `F7_Green_Quantity` | Green | 1 |
| Shift+F7 | `Shift_F7_Green_Quantity` | Green | 5 |
| F8 | `F8_Pink_Quantity` | Pink | 1 |
| Shift+F8 | `Shift_F8_Pink_Quantity` | Pink | 5 |
| F9 | `F9_Inferno_Quantity` | Inferno | 1 |

`F11` ativa localmente o Galaxy Reset pela mesma sequência do presente Galaxy. Não cria bola e não usa a posição do mouse. F10 continua reservado ao diagnóstico de encerramento.

`Shift+F9` não tem ação. Ctrl e Alt não são aceitos junto das hotkeys. A Inferno Ball tem parâmetros próprios documentados em [INFERNO.md](INFERNO.md).

## Limitações conhecidas

- O conector usa uma biblioteca TikTok não oficial. A conexão pública pode sofrer limite de taxa, falhar na consulta do catálogo ou encontrar a live offline. Compilar o projeto não comprova recebimento de presentes em uma live.
- Se o catálogo falhar, o fallback exige nome e custo exatos; presentes sem identificação confiável são ignorados. Um comando sem confirmação TCP não é reenviado automaticamente.
- O serviço aceita no máximo 100 bolas pendentes e processa até 5 por frame. Pedidos podem ser rejeitados ou concluir parcialmente se a cena, a posição ou a capacidade não permitirem o spawn.
- A cópia isolada do jogo compartilha o diretório de saves em `%USERPROFILE%\AppData\LocalLow\BATTLE LAB\10 Million Pixels Demo\save`. Faça backup antes de testar. O plugin não faz reset de saves. A Golden Ball não possui hotkey nesta versão.

## Feed de presentes no jogo

O conector envia, junto aos comandos TCP, o ID estável do espectador, nome público, ID e nome do presente, quantidade incremental e um ID de lote. O plugin correlaciona cada `requestId` com o resultado final do `BallSpawnService`. Somente bolas efetivamente criadas entram no feed; uma aceitação na fila não equivale a uma invocação. As hotkeys continuam sem metadados de presente e não geram mensagens.

Mensagens visíveis com o mesmo ID de espectador e ID de presente são atualizadas no mesmo elemento. Os comandos de até 20 bolas gerados por um único evento compartilham o ID de lote: os presentes são contados uma vez, enquanto as bolas são somadas conforme cada comando termina. O feed mostra até cinco mensagens na lateral direita e usa uma fonte do próprio jogo, preferindo `Perfect DOS VGA 437` quando disponível. A renderização usa TextMeshPro sem painel, sombra ou som. A duração e as animações usam tempo real do Unity. Se o evento não tiver ID estável de espectador ou nome público, as bolas ainda são solicitadas, mas o feed não atribui o envio a uma pessoa incerta.

As opções ficam em `[GiftFeed]` no arquivo `BepInEx/config/com.pixellive.mod.cfg`: `GiftFeedEnabled=true`, `GiftFeedDuration=5`, `GiftFeedFadeInDuration=0.2`, `GiftFeedFadeOutDuration=0.4`, `GiftFeedMaxVisible=5` e `GiftFeedFontSize=20`. O tamanho foi escolhido para a referência de 1920x1080 do CanvasScaler e pode ser ajustado se a escala real da transmissão exigir.

Para testar sem uma live, inicie a cópia isolada, entre numa cena jogável e execute `npm run build` seguido de `npm run simulate:feed` em `tiktok-connector`. O comando cria cinco bolas laranjas de teste; o primeiro espectador deve passar de `1 Rosa / 1 Bola Laranja` para `4 Rosas / 4 Bolas Laranjas` na mesma entrada. Um segundo espectador deve aparecer separadamente. Confira `BepInEx/LogOutput.log` para a fonte selecionada, as aceitações na fila e os resultados `created=`. Uma live real ainda depende de eventos disponíveis na conta conectada.

## PÓDIO DOS PIXELS

O pódio mostra os cinco espectadores cujas bolas de presentes destruíram mais pixels na sessão atual. Cada bola criada por um comando com ID estável de espectador recebe um componente de autoria. Hotkeys e bolas nativas não recebem esse componente. O placar usa o mesmo ID estável do feed; espectadores com nomes públicos iguais permanecem separados.

O rastreador observa `WorldManager.BreakTile` com Harmony e só atribui um ponto quando o tile passa de `type > 0` para `type == 0`. Danos parciais, células já vazias e mudanças sem uma origem de bola identificada não pontuam. Um contexto temporário, restaurado após cada chamada, acompanha a colisão nativa, os raios da Yellow Ball e a coroutine de veneno da Green Ball. Novos objetos de veneno herdam a autoria do objeto que os criou; essa associação é removida quando voltam ao pool. A explosão da Inferno Ball conserva a autoria da bola original até terminar, contando apenas os tiles realmente destruídos pelo dano nativo. Na versão atual do jogo, a Pink Ball destrói tiles pela colisão em `PhysicsAgent`, sem outro caminho de `DamageTile` identificado.

`PixelLeaderboardService` guarda todos os participantes e os totais em memória, independentemente de aparecerem no top 5. O ranking é ordenado somente nos intervalos de atualização visual; em empate, fica primeiro quem alcançou o total antes. O painel usa a mesma fonte selecionada pelo feed, moldura reta e fundo escuro, ouro/prata/bronze nas três primeiras posições, e aparece acima do feed na lateral direita. Os pontos não são gravados em disco. O pódio é zerado quando a vitória nativa é confirmada ou quando o jogador confirma `Settings → Reset All Data`; referências de bolas da partida anterior deixam de pontuar.

As opções `[PixelLeaderboard]` no arquivo `BepInEx/config/com.pixellive.mod.cfg` são `PixelLeaderboardEnabled=true`, `PixelLeaderboardMaxVisible=5` (1–5), `PixelLeaderboardRefreshInterval=0.2` e `PixelLeaderboardFontSize=20`.

Para verificar a integração sem presentes reais, feche o conector de live, inicie a cópia isolada, entre numa cena jogável e execute `npm run simulate:feed` em `tiktok-connector`. Esse simulador envia bolas de dois IDs de espectadores diferentes. Observe o pódio enquanto cada bola destrói pixels: o total deve crescer apenas quando uma célula desaparece, e as pontuações devem permanecer após as mensagens do feed sumirem. Para conferir a estrutura nativa usada pelo rastreador, execute `./tests/Inspect-NativeDestruction.ps1`; para os testes de contagem e ordenação, execute `dotnet run --project ./tests/PixelLeaderboardTests.csproj -c Release`.
