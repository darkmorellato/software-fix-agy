#!/usr/bin/env python3
"""E2E offline do servidor ODM (SLA) do software-fix-agy.

Sobe um mock HTTP do callsimunlockservice (como um servidor Motorola de mentira),
sobe o OdmSocketServer REAL via CLI apontado para o mock e valida o protocolo:

  [1] sonda "IsRunning"                      -> resposta "0"
  [2] assinatura "<tipo><hash hex>"          -> 512 bytes (256 da assinatura + 256 zeros)
      + confere o body/auth que chegou no mock (contrato do serviço)
  [3] falha de assinatura (URL inexistente)  -> 512 bytes zerados (como o app oficial)

Uso:
    dotnet build src/SoftwareFix.CLI -v q --nologo
    python3 tests/odm/test_odm_e2e.py [caminho/SoftwareFix.CLI.dll]
"""
import base64
import json
import os
import socket
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, HTTPServer

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DLL = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    ROOT, "src", "SoftwareFix.CLI", "bin", "Debug", "net8.0", "SoftwareFix.CLI.dll")

MOCK_PORT = 8787
SRV_PORT = 5091
SRV_PORT_BAD = 5092
USER = "tecnico.teste"
PASS = "senha-secreta-123"
SIG_HEX = "ab" * 256            # 512 chars hex -> 256 bytes de assinatura
PHONE_HASH_HEX = "deadbeef" * 8                    # hash de teste do aparelho (hex)
PHONE_HASH_BYTES = bytes.fromhex(PHONE_HASH_HEX)   # no socket, o hash trafega como BYTES
IMEI = "123456789012345"

received = {}


class MockHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length)
        received["path"] = self.path
        received["auth"] = self.headers.get("Authorization")
        received["body"] = json.loads(body.decode())
        resp = json.dumps({"responseCode": "0", "responseMsg": "ok", "msg": SIG_HEX}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(resp)))
        self.end_headers()
        self.wfile.write(resp)

    def log_message(self, format, *args):
        pass


def wait_port(port, timeout=30):
    end = time.time() + timeout
    while time.time() < end:
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=1):
                return True
        except OSError:
            time.sleep(0.2)
    return False


def request(port, payload):
    with socket.create_connection(("127.0.0.1", port), timeout=30) as s:
        s.sendall(payload)
        s.shutdown(socket.SHUT_WR)
        chunks = []
        while True:
            b = s.recv(4096)
            if not b:
                break
            chunks.append(b)
        return b"".join(chunks)


def start_server(port, odm_url):
    env = dict(os.environ,
               SOFTWARE_FIX_ODM_URL=odm_url,
               SOFTWARE_FIX_ODM_PASSWORD=PASS)
    return subprocess.Popen(
        ["dotnet", DLL, "odm-server", "--imei", IMEI, "--user", USER, "--port", str(port)],
        env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)


def main():
    failures = []
    mock = HTTPServer(("127.0.0.1", MOCK_PORT), MockHandler)
    threading.Thread(target=mock.serve_forever, daemon=True).start()

    srv = start_server(SRV_PORT, f"http://127.0.0.1:{MOCK_PORT}/callsimunlockservice")
    try:
        if not wait_port(SRV_PORT):
            print("FALHA: servidor ODM nao subiu")
            if srv.poll() is not None and srv.stdout is not None:
                print(srv.stdout.read())
            return 1

        # [1] sonda IsRunning -> "0"
        r = request(SRV_PORT, b"IsRunning")
        ok1 = r == b"0"
        print(f"[1] sonda IsRunning -> {r!r}  {'OK' if ok1 else 'FALHA'}")
        if not ok1:
            failures.append("sonda")

        # [2] assinatura: "<tipo><hash binario>" -> 512 bytes (256 sig + 256 zeros)
        r = request(SRV_PORT, b"1c" + PHONE_HASH_BYTES)
        ok2 = len(r) == 512 and r[:256] == bytes.fromhex(SIG_HEX) and r[256:] == b"\x00" * 256
        print(f"[2] assinatura -> {len(r)} bytes (256 sig + 256 zeros)  {'OK' if ok2 else 'FALHA'}")
        if not ok2:
            failures.append("assinatura")

        # contrato que chegou no "servico Motorola"
        exp_auth = "Basic " + base64.b64encode(f"{USER}:{PASS}".encode()).decode()
        body = received.get("body", {})
        checks = {
            "path == /callsimunlockservice": received.get("path") == "/callsimunlockservice",
            "Authorization Basic (user:senha)": received.get("auth") == exp_auth,
            "newIMEI": body.get("newIMEI") == IMEI,
            "mascid": body.get("mascid") == "GC-7777777-TEST-190",
            "clientIP": body.get("clientIP") == "99.99.99.99",
            "clientReqType": body.get("clientReqType") == "0x00",
            "rsd_log_id": body.get("rsd_log_id") == "91C44564934F4E96A1533641D2694B38",
            "prod_id": body.get("prod_id") == "M6102",
            "type": body.get("type") == "23",
            "keyname": body.get("keyname") == "1",
            "data == hex do hash do aparelho": body.get("data") == PHONE_HASH_HEX,
        }
        for name, ok in checks.items():
            print(f"    campo {name}: {'OK' if ok else 'FALHA'}")
            if not ok:
                failures.append(name)

        # [3] falha de assinatura -> 512 zeros
        srv_bad = start_server(SRV_PORT_BAD, "http://127.0.0.1:1/callsimunlockservice")
        try:
            if not wait_port(SRV_PORT_BAD):
                print("[3] FALHA: segundo servidor nao subiu")
                failures.append("srv-bad")
            else:
                r = request(SRV_PORT_BAD, b"1c" + PHONE_HASH_BYTES)
                ok3 = r == b"\x00" * 512
                print(f"[3] falha de assinatura -> {len(r)} bytes zerados  {'OK' if ok3 else 'FALHA'}")
                if not ok3:
                    failures.append("zeros")
        finally:
            srv_bad.terminate()
            srv_bad.wait(timeout=10)

    finally:
        srv.terminate()
        srv.wait(timeout=10)
        mock.shutdown()

    if failures:
        print("\nRESULTADO: FALHAS ->", failures)
        out = srv.stdout.read() if srv.stdout else ""
        print("--- saida do servidor ---")
        print(out)
        return 1

    print("\nRESULTADO: TODOS OS TESTES PASSARAM")
    return 0


if __name__ == "__main__":
    sys.exit(main())
