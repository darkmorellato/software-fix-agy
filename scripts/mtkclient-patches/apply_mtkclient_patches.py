#!/usr/bin/env python3
"""Aplica no mtkclient instalado os patches necessários para o fluxo do port (moto g17/lamuc).

Patches (idempotentes):
 1) xflash_lib.py — habilita CC_START_DL_INFO no writeflash (upstream deixa comentado);
 2) xflash_lib.py — adiciona o método download_partition (gravação via CMD_DOWNLOAD,
    formato extraído do trace do SP Flash Tool; necessário em DAs "FORBID" que recusam
    CMD_WRITE_DATA com 0xc002000c);
 3) mtk_da_handler.py — rota de da_write para o modo download quando
    SOFTWARE_FIX_MTK_DOWNLOAD=1 (ver docs/MTK_RESCUE.md).

Uso:
    python apply_mtkclient_patches.py [caminho/para/site-packages/mtkclient]
    (sem argumento: localiza o mtkclient importável no ambiente atual)

Observação: os arquivos originais são salvos com sufixo .bak na primeira aplicação.
"""
import importlib.util
import os
import shutil
import sys


def find_mtkclient():
    if len(sys.argv) > 1:
        return os.path.abspath(sys.argv[1])
    spec = importlib.util.find_spec("mtkclient")
    if spec is None or spec.origin is None:
        print("[-] mtkclient não encontrado (instale ou passe o caminho).")
        sys.exit(2)
    return os.path.dirname(spec.origin)


BASE = find_mtkclient()
XFLASH = os.path.join(BASE, "Library", "DA", "xflash", "xflash_lib.py")
DAH = os.path.join(BASE, "Library", "DA", "mtk_da_handler.py")
print(f"[*] mtkclient em: {BASE}")


def patch(path, old, new, marker, label):
    src = open(path, encoding="utf-8").read()
    if marker in src:
        print(f"[=] {label}: já aplicado")
        return
    if old not in src:
        print(f"[X] {label}: padrão não encontrado em {path}")
        sys.exit(3)
    if not os.path.exists(path + ".bak"):
        shutil.copy(path, path + ".bak")
    open(path, "w", encoding="utf-8").write(src.replace(old, new, 1))
    print(f"[+] {label}: aplicado")


# --- 1) START_DL_INFO no writeflash -----------------------------------------
patch(
    XFLASH,
    "        # self.send_devctrl(self.Cmd.START_DL_INFO)",
    "        self.send_devctrl(self.Cmd.START_DL_INFO)",
    "\n        self.send_devctrl(self.Cmd.START_DL_INFO)",
    "xflash_lib: START_DL_INFO habilitado",
)

# --- 2) método download_partition -------------------------------------------
DOWNLOAD_METHOD = '''    def download_partition(self, partitionname: str, data: bytes, display: bool = True) -> bool:
        """Gravação pelo caminho CMD_DOWNLOAD (0x010001) — formato extraído do trace do
        SP Flash Tool (resgate lamuc, GLB log de 09/10/2026):
          DOWNLOAD -> status; nome da partição -> tamanho (u64) -> status (parameter check);
          por chunk (<= write_packet_length): host_status(u32=0), checksum(u32 = soma dos
          bytes do chunk & 0xFFFF), dados (header+chunk) -> status; ao final: +1 status.
        Necessário em DAs 'FORBID' (ex.: DA_A15_lamuc_FORBID_SIGNED) que recusam
        CMD_WRITE_DATA com 0xc002000c 'Write data not allowed'."""
        if not data:
            self.error("No data to download")
            return False
        try:
            plen = self.get_packet_length()
            chunk_size = plen.write_packet_length or 0x200000
        except Exception:
            chunk_size = 0x200000
        name = partitionname.encode("ascii") if isinstance(partitionname, str) else partitionname
        self.info(f"Downloading \\"{partitionname}\\" ({len(data)} bytes, chunks of {hex(chunk_size)})")
        if not self.xsend(self.cmd.DOWNLOAD):
            self.error("Error on sending download cmd")
            return False
        status = self.status()
        if status != 0:
            self.error(f"Error on download cmd: {self.eh.status(status)}")
            return False
        if not self.xsend(name):
            self.error("Error on sending partition name")
            return False
        if not self.xsend(len(data), is64bit=True):
            self.error("Error on sending partition length")
            return False
        status = self.status()
        if status != 0:
            self.error(f"Error on download parameter check: {self.eh.status(status)}")
            return False
        pg = progress(total=len(data), prefix="Download:", guiprogress=self.mtk.config.guiprogress)
        pos = 0
        while pos < len(data):
            chunk = bytes(data[pos:pos + chunk_size])
            pos += len(chunk)
            if len(chunk) % 512:
                chunk += b"\\x00" * (512 - (len(chunk) % 512))
            checksum = sum(chunk) & 0xFFFF
            if not self.xsend(0):
                self.error("Error on sending host status")
                return False
            if not self.xsend(checksum):
                self.error("Error on sending checksum")
                return False
            if not self.xsend(chunk):
                self.error("Error on sending chunk")
                return False
            status = self.status()
            if status != 0:
                self.error(f"Error on download data: {self.eh.status(status)}")
                return False
            if display:
                pg.update(len(chunk))
        status = self.status()
        if status != 0:
            self.error(f"Error on download end: {self.eh.status(status)}")
            return False
        if display:
            pg.done()
        return True

'''

ANCHOR = "    def cmd_write_data(self, addr, size, storage=DaStorage.MTK_DA_STORAGE_EMMC,\n                       parttype=EmmcPartitionType.MTK_DA_EMMC_PART_USER):"
patch(
    XFLASH,
    ANCHOR,
    DOWNLOAD_METHOD + ANCHOR,
    "def download_partition",
    "xflash_lib: método download_partition",
)

# --- 3) rota no da_write -----------------------------------------------------
OLD_DA_WRITE = '''    def da_write(self, parttype: str, filenames: list, partitions: list):
        if len(partitions) != len(filenames):
            self.error("You need to gives as many filenames as given partitions.")
            self.close()
            exit(0)
        if parttype == "user" or parttype is None:'''
NEW_DA_WRITE = '''    def da_write(self, parttype: str, filenames: list, partitions: list):
        if len(partitions) != len(filenames):
            self.error("You need to gives as many filenames as given partitions.")
            self.close()
            exit(0)
        # Caminho CMD_DOWNLOAD para DAs 'FORBID' (ex.: DA_A15_lamuc_FORBID_SIGNED do moto g17),
        # ativado por SOFTWARE_FIX_MTK_DOWNLOAD=1 — ver docs/MTK_RESCUE.md.
        if (os.environ.get("SOFTWARE_FIX_MTK_DOWNLOAD") == "1"
                and hasattr(self.mtk.daloader.da, "download_partition")):
            for partition, partfilename in zip(partitions, filenames):
                if not os.path.exists(partfilename):
                    self.error(f"Filename {partfilename} doesn't exist")
                    continue
                with open(partfilename, "rb") as rf:
                    data = rf.read()
                if self.mtk.daloader.da.download_partition(partition, data):
                    print(f"Downloaded {partfilename} to partition {partition}.")
                else:
                    print(f"Failed to download {partfilename} to partition {partition}.")
            return
        if parttype == "user" or parttype is None:'''
patch(
    DAH,
    OLD_DA_WRITE,
    NEW_DA_WRITE,
    'os.environ.get("SOFTWARE_FIX_MTK_DOWNLOAD")',
    "mtk_da_handler: rota download no da_write",
)

print("[*] Concluído. Use SOFTWARE_FIX_MTK_DOWNLOAD=1 com o DA 'FORBID' ao gravar.")
