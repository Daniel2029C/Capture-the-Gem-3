#include "GameServer.h"

#include <csignal>
#include <cstdio>
#include <cstdlib>

static volatile bool g_running = true;

int main(int argc, char** argv) {
    uint16_t port = argc > 1 ? static_cast<uint16_t>(std::atoi(argv[1])) : 7777;
    std::signal(SIGINT, [](int) { g_running = false; });

    // TODO(you): the map hash should come from the loaded map file, and match config from the orchestrator.
    ctg::server::GameServer server(port, /*mapHash=*/0);
    if (!server.Start()) return 1;
    server.Run(g_running);
    std::printf("shut down\n");
    return 0;
}
