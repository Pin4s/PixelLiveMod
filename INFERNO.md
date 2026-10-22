# Inferno Ball — teste experimental

O atalho `F9` usa a posição do mouse capturada no momento do comando e a fila existente de `BallSpawnService`. A instância usa o prefab Orange como base, sem modificar o prefab. Apenas nessa instância, o dano de colisão comum fica desabilitado; `InfernoBall` inicia a explosão no primeiro impacto com terreno destrutível. O ciclo nativo de `Ball.Start()`/`Ball.OnDestroy()` continua gerenciando `BallManager.ballList`.

Feche o jogo e edite `BepInEx/config/com.pixellive.mod.cfg` **na cópia isolada**. Reinicie após cada mudança:

| Seção | Chave | Padrão | Faixa |
|---|---|---:|---:|
| Hotkeys | `F9_Inferno_Quantity` | 1 | 1..20 |
| Inferno | `InfernoExplosionRadius` | 100 células | 1..150 |
| Inferno | `InfernoDamage` | 1000000 | 1..1000000 |
| Inferno | `InfernoParticlesEnabled` | true | booleano |
| Inferno | `InfernoMaxParticles` | 320 | 0..1000 |
| Inferno | `InfernoTilesPerFrame` | 500 | 16..2000 |
| Inferno | `InfernoExplosionDuration` | 1.25 segundo | 0.1..10 |

Uma célula mede uma unidade do mundo. A área é circular pela distância do centro de cada célula ao ponto de impacto. `WorldManager.DamageTile()` e `BreakTile()` fazem o dano, atualizam dados/texturas e contabilizam cada pixel. O mod agrupa as atualizações dos objetos de chunk por frame. Apenas terreno comum de tipos 1..7 e o tipo envenenado 66 entram no efeito; ficam excluídos os tipos especiais 600, 700..799, 1000 e 12345. Portanto, pixels Golden, upgrades e proteções não são removidos por essa explosão.

Há no máximo duas explosões ativas e vinte aguardando. Se a capacidade acabar, a colisão não consome a bola; o mod registra o motivo e tenta novamente num contato posterior. O processamento respeita tanto `InfernoTilesPerFrame` como um teto adicional de aproximadamente 8 ms de CPU por frame e por explosão. A onda visual acompanha o raio efetivamente processado, mesmo que a duração real ultrapasse a meta configurada. Efeitos e tarefas são cancelados na troca de cena.

Faça backup de `%USERPROFILE%\AppData\LocalLow\BATTLE LAB\10 Million Pixels Demo\save` antes de executar a cópia isolada. Para o primeiro teste, configure raio 5 e quantidade 1; entre numa cena jogável, pressione `F9` e confira aparência, movimento, um único impacto, cratera, contabilização e saldo. No log, procure `Inferno detonation started` e `Inferno detonation complete`. Depois feche o jogo, restaure raio 100, reinicie e teste **uma** bola, acompanhando responsividade e `slowest frame`. `circle candidates` é a quantidade geométrica de células no círculo; `destroyed` conta apenas as células realmente quebradas. Confirme também que Shift+F9 não produz nenhum comando. Não dispare Golden Ball automaticamente.
