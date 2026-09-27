"""Offline checks for the 64-bit precision boundary and packet framing."""
import struct
import unittest

from shop_wire import decode_sell_shop


class ShopWireTests(unittest.TestCase):
    def packet(self):
        raw = bytearray(21 + 84)
        raw[0] = 0xA1
        struct.pack_into("<i", raw, 1, 12345)
        struct.pack_into("<I", raw, 17, 1)
        struct.pack_into("<ii", raw, 21, 67890, 13)
        struct.pack_into("<q", raw, 33, (1 << 32) + 7)
        struct.pack_into("<H", raw, 51, 12)
        struct.pack_into("<qq", raw, 89, (1 << 40) + 19, (1 << 33) + 23)
        return raw

    def test_preserves_high_quantity_and_price_bits(self):
        row = decode_sell_shop(self.packet())["rows"][0]
        self.assertEqual((row["quantity"], row["price"], row["base_price"], row["enchant"]),
                         ((1 << 32) + 7, (1 << 40) + 19, (1 << 33) + 23, 12))

    def test_rejects_incomplete_or_different_framing(self):
        packet = self.packet()
        for bad in (packet[:-1], packet + b"\0", b"\xA2" + packet[1:]):
            with self.subTest(length=len(bad), opcode=bad[0]):
                with self.assertRaises(ValueError):
                    decode_sell_shop(bad)


if __name__ == "__main__":
    unittest.main()
