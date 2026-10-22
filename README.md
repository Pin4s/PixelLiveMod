# PixelLiveMod

Plugin C# para **10 Million Pixels Demo** com BepInEx 5, acompanhado de um conector Node.js/TypeScript para presentes de lives públicas do TikTok. O plugin também oferece hotkeys locais para criar bolas. O jogo, seus assets e as DLLs do BepInEx não fazem parte deste repositório.

## Arquitetura

TikTok LIVE (presentes) → conector Node.js → JSON por TCP em `127.0.0.1:19081` → plugin Unity → fila de spawn.

O conector identifica presentes pelo catálogo da sala (nome/ID e custo em moedas), trata sequências e envia comandos de 1 a 20 bolas. O plugin aceita comandos na thread principal do Unity, coloca as bolas na fila e responde se **enfileirou** o pedido. A resposta não confirma a criação de todas as bolas; o resultado aparece em `BepInEx/LogOutput.log`. A ponte escuta somente na interface local.

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

`Shift+F9` não tem ação. Ctrl e Alt não são aceitos junto das hotkeys. A Inferno Ball tem parâmetros próprios documentados em [INFERNO.md](INFERNO.md).

## Limitações conhecidas

- O conector usa uma biblioteca TikTok não oficial. A conexão pública pode sofrer limite de taxa, falhar na consulta do catálogo ou encontrar a live offline. Compilar o projeto não comprova recebimento de presentes em uma live.
- Se o catálogo falhar, o fallback exige nome e custo exatos; presentes sem identificação confiável são ignorados. Um comando sem confirmação TCP não é reenviado automaticamente.
- O serviço aceita no máximo 100 bolas pendentes e processa até 5 por frame. Pedidos podem ser rejeitados ou concluir parcialmente se a cena, a posição ou a capacidade não permitirem o spawn.
- A cópia isolada do jogo compartilha o diretório de saves em `%USERPROFILE%\AppData\LocalLow\BATTLE LAB\10 Million Pixels Demo\save`. Faça backup antes de testar. O plugin não faz reset de saves. A Golden Ball não possui hotkey nesta versão.
