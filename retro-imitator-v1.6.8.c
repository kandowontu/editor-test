/*  Retro Imitator - 1.6.8 - 2025-12-30
    Copyright (C) 2025 Johnathan Roatch

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

To compile (with SDL 1.2 or sdl12-compat):
    gcc -O2 -std=c99 -Wall -Wextra -Wpedantic -lc -lm -o retro-imitator retro-imitator.c `sdl-config --cflags --libs`

Run with:
    ./retro-imitator rom_file.bin [-]

	if third argument is "-", then video recording will be pipped to stdout

    Or to attempt to compile as a libretro core, find libretro.h, use
    `-D__LIBRETRO__` and compiler options for dll/shared object output

Controls:
    Retrogress      R
    Quit            Esc
    Soft Reset      T
    Hard Reset      Shift+T
    Pause           P
    Frame           Space
    Fullscreen      F11
    Screenshot      F12
    Video Rec       Shift+F12
    Remove shading  Backspace
    Sprite Warp     \ (backslash)
Player 1:
    A               S
    B               A
    Select          Q
    Start           W
    Up              Keypad Up
    Down            Keypad Down
    Left            Keypad Left
    Right           Keypad Right
Player 2:
    A               ' (quote)
    B               ; (semicolon)
    Select          . (period)
    Start           / (slash)
    Up              I
    Down            K
    Left            J
    Right           L
Mute Audio Channels:
    pulse 1         1
    pulse 2         2
    organ           3
    noise           4
    ex pulse 1      5
    ex pulse 2      6
    ex saw          7
    PWM and PCM     8
Silly Switches
    Sinewave only   9
    Invert freq     0
    PWM bit reverse - (minus)
    Mode 12 IRQ fix = (equals)
*/
#include <stddef.h>
#include <limits.h>
#include <float.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdbool.h>

typedef int8_t s8;
typedef int16_t s16;
typedef int32_t s32;
typedef int64_t s64;
typedef uint8_t u8;
typedef uint16_t u16;
typedef uint32_t u32;
typedef uint64_t u64;

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

// --- Backend ---

#define RETRO_IMITATOR_INIT_MAGIC 0x57655025

typedef struct retro_imitator_cpu {
	u32 pc;
	u8 flags;  // The "always set" flag is 0: NMOS mode, 1: 65C02 mode
	u8 y;
	u8 a;
	u8 x;
	u8 sp;
	u8 irq; // BRK = 0, RST = 1, NMI = 2, IRQ = 4
} retro_imitator_cpu;

typedef struct retro_imitator_state {
	u32 init_magic;
	u32 cycles;
	retro_imitator_cpu cpu;
	u8 mem[0x6000];
	// 0x0000~0x0bff: General purpose RAM
	// 0x0c00~0x0cff: Sprite page for writes to system IO 0x401c
	// 0x0d00~0x0dff: unused
	// 0x0e00~0x0fdf: Save state of this struct
	// 0x0fe0~0x0fff: video palette
	// 0x1000~0x2fff: video tile RAM
	// 0x3000~0x3fff: video screen data
	// 0x4000~0x5fff: General purpose RAM at address 0x6000
	u8 mode20_ram[0x8000];
	u8* prg;
	size_t prg_size;
	u8* chr;
	size_t chr_size;
	u8 render_option;
	u8 audio_chn_mute;
// memory mapping
	u32 chr_seg_conf;  // configure each segment to map by .5k, 1k, 2k, 4k, or 8k (magic 0xedec1213)
	u16 prg_seg_conf;  // configure each segment to map by 4k, 8k, 16k, or 32k
	u16 prg_mode;
	u16 mode9_addr_check;
	u8 mode9_shift;
	bool mode12_irq;
	u8 mode20_reg;
	u8 mode4_reg[4];
	u8 mode9_reg[4];
	u8 mode12_reg[4];  // the other 8 are stashed in chr_bank
// memory banks
	u16 prg_bank[8];  // In 4096 byte segments
	u16 chr_bank[16];  // In 512 byte segments
// gamepad
	u16 p1_in;
	u16 p2_in;
	u16 p1;
	u16 p2;
// video
	u16 vid_mode;
	u16 vid_tile_ptr;
	u8 vid_sprite_page;
	u8 vid_sprite_ptr;
	u8 vid_scroll_x;
	u8 vid_scroll_y;
	u8 bg_index;
	u8 vsync_state;
	u8 nt_mir;
	u8 nt_mir_prev_byte;
// audio state
	u32 audio_chn_info[8];
	u16 audio_chn_length[4];
	u8 audio_chn_enable;
	bool audio_hi_carry;
	bool pwm_triggered;
	u8 pwm_rate;
	u16 pwm_len;
	u16 pwm_addr;
	u32 pwm_playhead;
	u16 raw_pcm_len;
	u8 raw_pcm[512]; // only first byte is serialized
// audio gen
	double audio_chn_phase[7];
	u64 snd_gen_rand;
	double x1;
	double x2;
	double x3;
	double y1;
	double y2;
	double y3;
	double high_pass_filter_x;
	double high_pass_filter_y;
} retro_imitator_state;

static u64 retro_imitator_round_pot(u64 v)
{
	v--;
	v |= v >> 1;
	v |= v >> 2;
	v |= v >> 4;
	v |= v >> 8;
	v |= v >> 16;
	v |= v >> 32;
	v++;
	return v;
}

// pins; 1: write, 2: opcode fetch, 4: vector pull, 8: MLB
u8 retro_imitator_bus_io (retro_imitator_state *this, u32 addr, u8 byte, u8 pins)
{
	if (!this || this->init_magic != RETRO_IMITATOR_INIT_MAGIC) return 0;
	if (addr >= 0x10000) return 0;
	bool write = (pins & 1) ? true : false;
	bool do_side_effects = ((pins & 0xe) != 0xa);  // opcode + MLB is normaly nonsense, use this to suppress mapper functions

	if (do_side_effects) this->cycles++;

	if ((pins & 4) && (addr == 0xfffd)) {
		// reset video sub-system on Reset vector pull
		this->vid_mode = 0;
		this->vid_scroll_x = 0;
		this->vid_scroll_y = 0;
		this->vsync_state = 0;
		this->nt_mir = 0;
		this->nt_mir_prev_byte = 0;
		this->audio_chn_enable = 0;
		for (int c = 0; c < 8; ++c) this->audio_chn_info[c] = 0;
		for (int c = 0; c < 4; ++c) this->audio_chn_length[c] = 0;
		this->raw_pcm_len = 0;
		this->pwm_len = 0;
		if ((this->prg_mode & 0x1f) < 4) {
			this->prg_seg_conf = 0x0000;
			this->chr_seg_conf = 0xedec1213;
			if ((this->prg_mode & 0x1f) == 2) {
				this->prg_seg_conf = 0x5555;
				this->prg_bank[4] = 0xffff;
			}
		}
	}

	// Redirect vid registers overlay to System state page
	if (do_side_effects && (0x2000 <= addr && addr < 0x2008)) addr = addr - 0x2000 + 0x4018;

	if (do_side_effects && 0x4000 <= addr && addr < 0x4100) {
		if (addr < 0x4014) {
			int channel = (addr - 0x4000) >> 2;
			if (channel == 4) channel = 7;
			int shift = ((addr - 0x4000) & 3) * 8;
			if (this->audio_hi_carry && (addr == 0x4001 || addr == 0x4005)) {
				u32 add_val = this->audio_chn_info[channel];
				add_val += (add_val & 0x00000800) ? -0x01000000 : +0x01000000;
				this->audio_chn_info[channel] = (this->audio_chn_info[channel] & ~0x07000000) | (add_val & 0x07000000);
			}
			this->audio_hi_carry = false;
			if (write) this->audio_chn_info[channel] = (this->audio_chn_info[channel] & ~(0xff << shift)) | ((u32)byte << shift);
			if (addr == 0x4010) this->pwm_triggered = true;
			if (addr == 0x4011 && !(pins & 8)) {
				this->raw_pcm[this->raw_pcm_len] = byte;
				this->raw_pcm_len = (this->raw_pcm_len + 1) % 512;
			}
#ifdef __EMSCRIPTEN__
			const u8 lcd[32] = "h\x9cv`Jf2d\xc2j^hlnxlnrzpRv\x02t\xa2z*xr~B|";
#else
			const u8 __attribute__ ((nonstring)) lcd[32] = "h\x9cv`Jf2d\xc2j^hlnxlnrzpRv\x02t\xa2z*xr~B|";
#endif
			if ((channel < 4) && (addr & 0x13) == 3) {
				this->audio_chn_length[channel] = lcd[(byte & 0xf8) >> 3]^98;
			}
			return (this->audio_chn_info[channel] & (0xff << shift)) >> shift;
		}
		const u8 v[5] = {0xe0, 0x60, 0x00, 0x20, 0x40};
		switch (addr - 0x4000) {
		break; case 0x14:
			byte = ((0x60 <= byte) && (byte < 0x80)) ? (byte - 0x20) : (byte & 0x3f);
			if (write) this->vid_sprite_page = byte;

			return this->vid_sprite_page;
		break; case 0x15:
			if (write) {
				this->audio_chn_enable = (this->audio_chn_enable & 0xc0) | (byte & 0x3f);
			}
			this->cpu.irq &= ~4;
			return this->audio_chn_enable;
		break; case 0x16:
			if (write && (byte & 0x01)) {
				this->p1 = this->p1_in;
				this->p2 = this->p2_in;
			}
			byte = (this->p1 & 0x01) | 0x40;
			if (!write) this->p1 = ((this->p1 & 0xfffe) >> 1) | 0x8000;
			return byte;
		break; case 0x17:
			if (write) {
				this->audio_hi_carry = ((this->audio_chn_enable & 0xc0) == 0x40 && (byte & 0xc0) == 0xc0);
				this->audio_chn_enable = (this->audio_chn_enable & 0x3f) | (byte & 0xc0);
			}
			byte = (this->p2 & 0x01) | 0x40;
			if (!write) this->p2 = ((this->p2 & 0xfffe) >> 1) | 0x8000;
			return byte;
		break; case 0x18:
			if (write) this->vid_mode = (this->vid_mode & 0xff00) | byte;
			return (this->vid_mode & 0x00ff) >> 0;
		break; case 0x19:
			if (write) this->vid_mode = (this->vid_mode & 0x00ff) | ((u16)byte << 8);
			return (this->vid_mode & 0xff00) >> 8;
		break; case 0x1a:
			if (!write) this->vsync_state = (this->vsync_state + 1)%5;
			return v[this->vsync_state];
		break; case 0x1b:
			if (write) this->vid_sprite_ptr = byte;
			return this->vid_sprite_ptr;
		break; case 0x1c:
			if (write) {
				this->vid_sprite_page = 0x0c;
				this->mem[0x0c00 + this->vid_sprite_ptr++] = byte;
			}
			return this->mem[0x0c00 + this->vid_sprite_ptr];
		break; case 0x1d:
			if (write) {
				this->vid_scroll_x = this->vid_scroll_y;
				this->vid_scroll_y = byte;
			}
			return this->vid_scroll_y;
		break; case 0x1e:
			if (write) {
				this->vid_tile_ptr = ((this->vid_tile_ptr & 0x00ff) << 8) | byte;
				byte = this->vid_scroll_x;
				this->vid_scroll_x = this->vid_scroll_y;
				this->vid_scroll_y = byte;
			}
			return this->vid_tile_ptr;
		break; case 0x1f:
		{
			u16 temp = (this->vid_tile_ptr & 0x3fff);
			if (!write && temp < 0x3fe0) temp = (temp - ((this->vid_mode & 0x04) ? 32 : 1)) & 0x3fff;
			if (0x3000 <= temp) temp |= 0x3fe0;
			temp = (temp + 0x1000) & 0x3fff;
			if (write) {
				this->mem[temp] = byte;
				if (temp < 0x1000 && ((temp & 0x0f) == 0)) this->bg_index = temp & 0x10;
				if (0x3000 <= temp && temp < 0x4000) {
					if (byte != this->nt_mir_prev_byte || this->nt_mir) this->nt_mir |= 1 << ((temp & 0x0c00) >> 10);
					const int nt_mir_act_mask[16] = {7, 7, 7, 2, 7, 1, 3, 0, 7, 3, 1, 0, 2, 0, 0, 0};
					int act = nt_mir_act_mask[this->nt_mir&0x0f];
					if (this->nt_mir & 0x70) act = (this->nt_mir & 0x70)>>4;
					if (act & 1) this->mem[temp ^ 0x0400] = byte;
					if (act & 2) this->mem[temp ^ 0x0800] = byte;
					if (act & 4) this->mem[temp ^ 0x0c00] = byte;
					this->nt_mir_prev_byte = byte;  // so that address wide clears won't pollute the screen arrangement detection
				}
			}
			this->vid_tile_ptr += (this->vid_mode & 0x04) ? 32 : 1;
			if (0x1000 <= temp && temp < 0x3000 && (512 <= this->chr_size)) {
				temp = (temp - 0x1000) & 0x1fff;
				int chr_mask = retro_imitator_round_pot(this->chr_size) - 1;
				int tlb_slot = temp/0x200;
				int b = ((this->chr_seg_conf >> (tlb_slot*2)) & 0x03) + 1;
				if (this->chr_seg_conf == 0xedec1213) b = 0;
				tlb_slot = tlb_slot & ~(0x0f >> b);
				u8* chr = (this->chr_seg_conf == 0xedec1213 && this->chr_size <= 8192) ? &this->mem[0x1000] : this->chr;
				return chr[(this->chr_bank[tlb_slot]*(0x2000>>b) + (temp&(0x1fff>>b))) & chr_mask];
			}
			return this->mem[temp];
		}
		break; default:
		break;
		}
		return 0x40;
	}
	if (this->prg == this->mode20_ram && 0x4100 <= addr && addr < 0x6000) {
		return byte;
	}
	if (do_side_effects && 0x4100 <= addr && addr < 0x41ff) {
		if ((this->prg_mode & 0x1f) != 20) {
			this->prg_seg_conf = 0x0000;
			this->chr_seg_conf = 0xedec1213;
			this->prg_bank[0] = 0x0000;
			this->chr_bank[0] = 0x0000;
			this->prg_mode = 20 | 0x2000 | 0x1000;
			this->mode20_reg = 0;
		}
		if (write) {
			if (addr == 0x4100) {
				const u16 t[8] = {0x0000, 0x5555, 0xaa55, 0xaaaa, 0xffff, 0xffff, 0xffff, 0xffff};
				this->prg_seg_conf = t[byte & 0x07];
			}
			if (addr == 0x4120) {
				const u32 t[8] = {0xedec1213, 0x00000000, 0x55555555, 0xaaaaaaaa, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff};
				this->chr_seg_conf = t[byte & 0x07];
			}
			if (0x4108 <= addr && addr < 0x4110) this->prg_bank[addr - 0x4108] = (this->prg_bank[addr - 0x4108] & 0x00ff) | (byte << 8);

			if (addr == 0x4115) this->mode20_reg = byte&1;

			if (0x4118 <= addr && addr < 0x4120) this->prg_bank[addr - 0x4118] = (this->prg_bank[addr - 0x4118] & 0xff00) | byte;
			if (0x4130 <= addr && addr < 0x4140) this->chr_bank[addr - 0x4130] = (this->chr_bank[addr - 0x4130] & 0x00ff) | (byte << 8);
			if (0x4140 <= addr && addr < 0x4150) this->chr_bank[addr - 0x4140] = (this->chr_bank[addr - 0x4140] & 0xff00) | byte;

			if (addr == 0x41A0) this->audio_chn_info[4] = (this->audio_chn_info[4] & ~0x000000ff) | ((u32)byte << (0*8));
			if (addr == 0x41A1) this->audio_chn_info[4] = (this->audio_chn_info[4] & ~0x00ff0000) | ((u32)byte << (2*8));
			if (addr == 0x41A2) this->audio_chn_info[4] = (this->audio_chn_info[4] & ~0xff000000) | ((u32)byte << (3*8));
			if (addr == 0x41A3) this->audio_chn_info[5] = (this->audio_chn_info[5] & ~0x000000ff) | ((u32)byte << (0*8));
			if (addr == 0x41A4) this->audio_chn_info[5] = (this->audio_chn_info[5] & ~0x00ff0000) | ((u32)byte << (2*8));
			if (addr == 0x41A5) this->audio_chn_info[5] = (this->audio_chn_info[5] & ~0xff000000) | ((u32)byte << (3*8));
			if (addr == 0x41A6) this->audio_chn_info[6] = (this->audio_chn_info[6] & ~0x000000ff) | ((u32)byte << (0*8));
			if (addr == 0x41A7) this->audio_chn_info[6] = (this->audio_chn_info[6] & ~0x00ff0000) | ((u32)byte << (2*8));
			if (addr == 0x41A8) this->audio_chn_info[6] = (this->audio_chn_info[6] & ~0xff000000) | ((u32)byte << (3*8));
		}
		return 0x00;
	}
	if ((this->prg_mode & 0x1f) == 20) {
		if (0x4200 <= addr && addr < 0x5000) {
			if (write) this->mem[(addr-0x3000+0x1000) & 0x3fff] = byte;
			return this->mem[(addr-0x3000+0x1000) & 0x3fff];
		}
		if (0x5000 <= addr && addr < 0x6000) {
			int banked_addr = addr-0x5000 + ((int)(this->mode20_reg)*0x1000);
			if (write) {
				this->mem[(banked_addr+0x1000) & 0x3fff] = byte;
				this->mem[(banked_addr&0x0fff) + 0x3000] = byte;
			}
			return this->mem[(banked_addr+0x1000) & 0x3fff];
		}
		if (0x6000 <= addr && addr < 0x8000) {
			if (write) this->mode20_ram[addr - 0x6000] = byte;
			return this->mode20_ram[addr - 0x6000];
		}
	} else {
		if (0x5000 <= addr && addr < 0x6000) {
			if (do_side_effects && write) {
				if ((this->prg_mode & 0x04) || ((byte & 0x7e) == 0x00)) this->prg_mode = 0x2000 | 0x1000 | 4 | ((byte & 0x80)>>6) | (byte & 0x01);
			}
			return byte;
		}
		if (0x6000 <= addr && addr < 0x8000) {
			if (write) this->mem[addr - 0x6000 + 0x4000] = byte;
			return this->mem[addr - 0x6000 + 0x4000];
		}
	}
	if ((0x8000 <= addr) && (0 < this->prg_size)) {
		if (do_side_effects && !write && ((this->prg_mode & 0x001f) < 4)) {
			// for mode 6 and 7 probing, disable each if reads occur in undefined areas
			if (!(this->prg_mode & 0x1000) && (addr < 0xc000) && (32768 < this->prg_size)) {
				this->prg_mode |= 0x1000;
				if ((this->prg_mode & 0x0200) && (this->prg_mode & 0x0001)) this->cpu.irq |= 1; // 32k bank modes need CPU reset
			}
			if (!(this->prg_mode & 0x2000)) {
				if ((0xa000 <= addr) && (addr < 0xbfff)) {
					this->prg_mode |= 0x2000;
					if ((this->prg_mode & 0x0200) && (this->prg_mode & 0x0001)) this->cpu.irq |= 1; // 32k bank modes need CPU reset
				}
			}
		}
		if (do_side_effects && write) {
			if ((this->prg_mode & 0x001f) < 4) {
				if ((this->prg_mode & 0x001f) == 0) {
					this->chr_bank[0] = byte;
				}
				if ((this->prg_mode & 0x001f) == 1) {
					this->chr_bank[0] = (byte & 0xf0) >> 4;
					this->prg_bank[0] = byte & 0x0f;
				}
				if ((this->prg_mode & 0x001f) == 2) {
					this->prg_bank[0] = byte;
					this->prg_bank[4] = 0xffff;
				}
				if ((this->prg_mode & 0x001f) == 3) {
					this->prg_bank[0] = byte;
				}
				if (!(this->prg_mode & 0x1000)) { // mode 9 probing
					if ((byte & 0x80) || (pins & 8)) { // Also reset shift if INC abs
						this->mode9_shift = 0x80;
					} else {
						if (!this->mode9_shift) this->prg_mode |= 0x1000; // shift register needs to reset first
						if (this->mode9_shift == 0x80) this->mode9_addr_check = addr;
						if (addr != this->mode9_addr_check) this->prg_mode |= 0x1000;  // the address for the 5 serial writes must match
						this->mode9_shift = ((this->mode9_shift & 0xff) >> 1) | ((byte & 1) << 7);
					}
					if (this->mode9_shift & 0x07) {
						int reg = (addr & 0x6000) >> 13;
						int val = (this->mode9_shift & 0xf8) >> 3;
						this->mode9_reg[reg] = val;
						this->mode9_shift = 0x80;
						if ((reg == 0) && ((val & 0x0c) != 0x0c) && (32768 < this->prg_size)) this->prg_mode |= 0x1000; // oops we just switched to a undefined bank
						if ((reg == 3) || (this->prg_size <= 32768) || ((this->prg_mode & 0x1f) == 1)) {
							// wrote bank number, switch to mode 9 is successful
							// if not set, reg 0 is assumed 0x0c (aka fixed $C000 16k banking)
							// also if a 32k prg game, assume any compleated serial write a success
							this->prg_mode = 9 | 0x2000; // mode 9 excludes mode 12
						}
					}

					if (!(this->prg_mode & 0x1000)) {
						// fix 0xc000 to the last bank until this is successful or disabled
						this->prg_seg_conf = (this->prg_seg_conf & 0x00ff) | 0x5500;
						this->prg_bank[4] = 0xffff;
						// also fix 0x8000 to the second to last bank if total prg size is just 32k
						// Becasue some games of that size assume it'll be there.
						if (this->prg_size <= 32768) {
							this->prg_seg_conf = (this->prg_seg_conf & 0xff00) | 0x0055;
							this->prg_bank[0] = 0xfffe;
						}
						this->prg_mode |= 0x0200; // flag that this probe corrupted prg mapping
					}

					// 32k bank modes 1 or 3, just got disabled, and prg mapping is corrupted
					if ((this->prg_mode & 0x01) && (this->prg_mode & 0x1000) && (this->prg_mode & 0x0200)) this->cpu.irq |= 1;  // Reset CPU without this probe
				}
				if (!(this->prg_mode & 0x2000)) { // mode 12 probing
					if (pins & 8) this->prg_mode |= 0x2000; // disable due to RWM opcode
					int reg = ((addr & 0x6000) >> 12) | (addr & 0x0001);
					if (reg < 4) this->mode12_reg[reg] = byte;

					if (reg == 0) this->prg_mode |= 0x0100; // flag that we have selected a bank
					if (reg == 1) {
						int bank = this->mode12_reg[0] & 7;
						// disable if a write to 0x8001 before selecting with 0x8000
						if (!(this->prg_mode & 0x0100)) this->prg_mode |= 0x2000;
						this->chr_bank[bank*2+1] = byte;
						if (bank == 6) this->prg_mode |= 0x0040; // flag that we set 1'st prg bank
						if (bank == 7) this->prg_mode |= 0x0080; // flag that we set 2'nd prg bank
					}
					if (reg == 4 || reg == 5 || reg == 7) this->prg_mode |= 0x2000; // disable due to IRQ before PRG
					// This also helps distinguish from prg modes that usually write to >= 0xc000

					if (!(this->prg_mode & 0x2000) && (this->prg_mode & 0x0100) && (this->prg_mode & 0x00c0)) {
						// not disabled, correctly selected a bank, and one of the prg banks was set
						// switch to mode 12 is successful
						this->prg_mode = 12 | 0x1000; // mode 12 excludes mode 9
					}

					if (!(this->prg_mode & 0x2000)) {
						// fix 0xe000 to the last bank until this is successful or disabled
						this->prg_seg_conf = (this->prg_seg_conf & 0x0fff) | 0xa000;
						this->prg_bank[6] = 0xffff;
						// also fix second to last bank after setting bank mode
						if (this->prg_mode & 0x0100) {
							this->prg_seg_conf = (this->prg_seg_conf & 0xf0f0) | 0x0a0a;
							this->prg_bank[0] = 0xfffe;
							this->prg_bank[4] = 0xfffe;
						}
						this->prg_mode |= 0x0200; // flag that this probe corrupted prg mapping
					}

					// 32k bank modes 1 or 3, just got disabled, and prg mapping is corrupted
					if ((this->prg_mode & 0x01) && (this->prg_mode & 0x2000) && (this->prg_mode & 0x0200)) this->cpu.irq |= 1;  // Reset CPU without this probe
					// After setting bank mode, all the banks got corrupted
					if ((this->prg_mode & 0x2000) && (this->prg_mode & 0x0100)) this->cpu.irq |= 1;
				}
			}
			if ((this->prg_mode & 0x14) == 0x04) {
				int reg = this->prg_mode & 0x3;
				this->mode4_reg[reg] = byte;
				if (reg == 2) {
					this->nt_mir = 0x70;
					if ((this->mode4_reg[2] & 0x3) == 2) this->nt_mir = 0x20;
					if ((this->mode4_reg[2] & 0x3) == 3) this->nt_mir = 0x10;
				}
				// Ignoring CHR RAM banking
				int prg_bank1 = this->mode4_reg[1] & 0x0f;
				int prg_bank2 = this->mode4_reg[1] & 0x0f;
				if ((this->mode4_reg[2] & 0x0c) == 0x00 || (this->mode4_reg[2] & 0x0c) == 0x04) {
					prg_bank1 = (prg_bank1 << 1) | 0;
					prg_bank2 = (prg_bank2 << 1) | 1;
				}
				int prg_mask = 0x0f >> (3-((this->mode4_reg[2] & 0x30) >> 4));
				prg_bank1 = (prg_bank1 & prg_mask) | ((this->mode4_reg[3] << 1) & ~prg_mask);
				prg_bank2 = (prg_bank2 & prg_mask) | ((this->mode4_reg[3] << 1) & ~prg_mask);
				if ((this->mode4_reg[2] & 0x0c) == 0x08) prg_bank1 = (this->mode4_reg[3] << 1) | 0;
				if ((this->mode4_reg[2] & 0x0c) == 0x0c) prg_bank2 = (this->mode4_reg[3] << 1) | 1;
				this->prg_seg_conf = 0x5555;
				this->prg_bank[0] = prg_bank1;
				this->prg_bank[4] = prg_bank2;
			}
			if ((this->prg_mode & 0x001f) == 9) {
				if (!(this->prg_mode & 0x0200)) {
					if ((byte & 0x80) || (pins & 8)) {
						this->mode9_reg[0] |= 0x0c;
						this->mode9_shift = 0x80;
					} else {
						this->mode9_shift = (this->mode9_shift >> 1) | ((byte & 1) << 7);
					}
				}
				this->prg_mode &= ~0x0200;
				if (this->mode9_shift & 0x07) {
					int reg = (addr & 0x6000) >> 13;
					int val = (this->mode9_shift & 0xf8) >> 3;
					if (reg == 0) {
						this->nt_mir = 0x70;
						if ((val & 0x3) == 2) this->nt_mir = 0x20;
						if ((val & 0x3) == 3) this->nt_mir = 0x10;
					}
					this->mode9_reg[reg] = val;
					this->mode9_shift = 0x80;
				}
				if (this->mode9_shift == 0x80) {
					int prg_bank = this->mode9_reg[3] & 0x0f;
					switch ((this->mode9_reg[0] & 0x0c) >> 2) {
					case 0: case 1:
						prg_bank = prg_bank >> 1;
						this->prg_seg_conf = 0x0000;
						this->prg_bank[0] = prg_bank;
					break; case 2:
						this->prg_seg_conf = 0x5555;
						this->prg_bank[0] = 0;
						this->prg_bank[4] = prg_bank;
					break; case 3:
						this->prg_seg_conf = 0x5555;
						this->prg_bank[0] = prg_bank;
						this->prg_bank[4] = 0xffff;
					break;
					}
					if (this->mode9_reg[0] & 0x10) {
						this->chr_seg_conf = 0x00000000;
						this->chr_bank[0] = this->mode9_reg[1];
						this->chr_bank[8] = this->mode9_reg[2];
					} else {
						this->chr_seg_conf = 0xedec1213;
						this->chr_bank[0] = this->mode9_reg[1] >> 1;
					}
				}
			}
			if ((this->prg_mode & 0x001f) == 12) {
				int reg = ((addr & 0x6000) >> 12) | (addr & 0x0001);
				if (reg < 4) this->mode12_reg[reg] = byte;
				if (reg == 1) {
					int bank = this->mode12_reg[0] & 7;
					this->chr_bank[bank*2+1] = byte;
				}
				this->prg_mode &= ~0x0200;

				if (reg == 6) {
					this->mode12_irq = false;
					this->cpu.irq &= ~4;
				}
				if (reg == 7) this->mode12_irq = true;

				this->nt_mir = (this->mode12_reg[2] & 0x01) ? 0x10 : 0x20;
				this->prg_seg_conf = 0xaaaa;
				this->prg_bank[0] = 0xfffe;
				this->prg_bank[2] = this->chr_bank[7*2+1];
				this->prg_bank[4] = 0xfffe;
				this->prg_bank[6] = 0xffff;
				this->prg_bank[(this->mode12_reg[0] & 0x40) ? 4 : 0] = this->chr_bank[6*2+1];

				bool chr_inv = (this->mode12_reg[0] & 0x80);
				this->chr_seg_conf = 0xaaaaaaaa;
				this->chr_bank[chr_inv ?  8 :  0] = (this->chr_bank[0*2+1] & 0xfe) + 0;
				this->chr_bank[chr_inv ? 10 :  2] = (this->chr_bank[0*2+1] & 0xfe) + 1;
				this->chr_bank[chr_inv ? 12 :  4] = (this->chr_bank[1*2+1] & 0xfe) + 0;
				this->chr_bank[chr_inv ? 14 :  6] = (this->chr_bank[1*2+1] & 0xfe) + 1;
				this->chr_bank[chr_inv ?  0 :  8] = this->chr_bank[2*2+1];
				this->chr_bank[chr_inv ?  2 : 10] = this->chr_bank[3*2+1];
				this->chr_bank[chr_inv ?  4 : 12] = this->chr_bank[4*2+1];
				this->chr_bank[chr_inv ?  6 : 14] = this->chr_bank[5*2+1];
			}
		}

		int prg_mask = retro_imitator_round_pot(this->prg_size) - 1;
		int tlb_slot = (addr & 0x7000) >> 12;
		int b = (this->prg_seg_conf >> (tlb_slot*2)) & 0x0003;  // b00 = 32K, b11 = 4k
		tlb_slot = tlb_slot & ~(0x07 >> b);
		if (((this->prg_mode & 0x1f) == 20) && (this->prg_bank[tlb_slot] & 0x8000)) {
			u8* ram_ptr = &this->mode20_ram[(this->prg_bank[tlb_slot]*(0x8000>>b) + (addr&(0x7fff>>b))) & 0xffff];
			if (write) *ram_ptr = byte;
			return *ram_ptr;
		}
		u8 *prg_ptr = &this->prg[(this->prg_bank[tlb_slot]*(0x8000>>b) + (addr&(0x7fff>>b))) & prg_mask];
		if (write && (this->prg == this->mode20_ram)) *prg_ptr = byte;  // RAM Cart
		return *prg_ptr;
	}

	// all else is just normal memory
	if (write) this->mem[addr & 0x3fff] = byte;
	return this->mem[addr & 0x3fff];
}

void retro_imitator_cpu_next(retro_imitator_state* this)
{
	if (!this || this->init_magic != RETRO_IMITATOR_INIT_MAGIC) return;
	// TODO: implement decimal mode
	retro_imitator_cpu* cpu = &this->cpu;
	u8 (*io) (retro_imitator_state*, u32, u8, u8) = retro_imitator_bus_io;
	u8 opcode = 0x00;
	u16 oparand = 0;
	u8 data = 0;
	bool ill_block = false;

	u8 interrupt = cpu->irq & 7;
	if (cpu->flags & 4) interrupt &= ~4;  // inhibit IRQ on CPU flag
	if (!interrupt) {
		opcode = io(this, cpu->pc++, 0, 0x2);
		ill_block = (opcode & 0x03) == 0x03;
		if (opcode == 0xeb) opcode = 0x65;  // unofficial sbc to adc for no reason
		if (ill_block) opcode &= ~0x02; // remap unofficial opcodes
		if (cpu->flags & 0x20) ill_block = false;
		int opcode_length = 2;  // most common length
		if ((opcode & 0x0c) == 0x0c) opcode_length = 3; // Absolute
		if ((opcode & 0x18) == 0x18) opcode_length = 3; // Absolute,x/y
		if ((opcode & 0x0d) == 0x08) opcode_length = 1; // implied
		if ((opcode & 0x9f) == 0x00) opcode_length = 1; // BRK RTI RTS
		if (opcode == 0x20) opcode_length = 3; // JSR absolute

		if (opcode_length > 1) oparand |= io(this, cpu->pc++, 0, 0x0) << 0;
		if (opcode_length > 2) oparand |= io(this, cpu->pc++, 0, 0x0) << 8;
	}
	int oparation = ((opcode & 0xe0) >> 5) | ((opcode & 0x01) << 4) | ((opcode & 0x02) << 2);
	int addressing_mode = (opcode & 0x1c) >> 2;
	// ["(d,x)", "d", "#i", "a", "(d),y", "d,x", "a,y", "a,x", "(d)", "d,y"]

	if (opcode == 0x00) { // brk or interrupt
		u16 vec = 0xfffe;
		if (interrupt) {
			cpu->irq &= ~(1|2);  // TODO: make NMI egde sensitive
			if (interrupt & 2) vec = 0xfffa;
			if (interrupt & 1) vec = 0xfffc;
			if (interrupt & 1) cpu->flags |= 0x10;
		}
		if (!interrupt) cpu->pc++;
		io(this,  0x0100 + cpu->sp--, (cpu->pc & 0xff00) >> 8, 0x1);
		io(this,  0x0100 + cpu->sp--, cpu->pc & 0xff, 0x1);
		io(this,  0x0100 + cpu->sp--, (cpu->flags | 0x30) & (interrupt ? ~0x10 : ~0), 0x1);
		cpu->flags = (cpu->flags & 0xf3) | 0x04;  // Set Interrupt Disable, clear decimal
		oparand = io(this, vec, 0, 0x4) << 0;
		oparand |= io(this, vec+1, 0, 0x4) << 8;
		cpu->pc = oparand;
		return;
	}

	if (ill_block && opcode == (0xcb&~0x02)) {  // AXS #i
		data = oparand & 0xff;
		int temp = (cpu->a & cpu->x) - data;
		data = temp;
		cpu->x = data;
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | ((temp >= 0) ? 0x01 : 0x00);
	}

	if ((opcode & 0x05) == 0x00) {
		if ((opcode & 0x0d) == 0x08) {
			// single byte opcodes
			int temp;
			if (opcode & 0x10) oparation += 16;
			switch (oparation) {
			case 0:         // php
				io(this,  0x0100 + cpu->sp--, (cpu->flags | 0x30) & ~0x10, 0x1);
			return; case 1:  // plp
				cpu->flags = (cpu->flags & 0x30) | (io(this, ++cpu->sp + 0x0100, 0, 0x0) & 0xcf);
			return; case 2:  // pha
				io(this,  0x0100 + cpu->sp--, cpu->a, 0x1);
			return; case 3:  // pla
				data = cpu->a = io(this, ++cpu->sp + 0x0100, 0, 0x0);
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 4:  // dey
				data = cpu->y -= 1;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 5:  // tay
				data = cpu->y = cpu->a;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 6:  // iny
				data = cpu->y += 1;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 7:  // inx
				data = cpu->x += 1;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 8:  // asl
				temp = (cpu->a & 0x80) >> 7;
				cpu->a = cpu->a << 1;
				cpu->flags = (cpu->flags & 0x7c) | (cpu->a & 0x80) | ((cpu->a == 0) ? 0x02 : 0x00) | temp;
			return; case 9:  // rol
				temp = (cpu->a & 0x80) >> 7;
				cpu->a = (cpu->a << 1) | (cpu->flags & 1);
				cpu->flags = (cpu->flags & 0x7c) | (cpu->a & 0x80) | ((cpu->a == 0) ? 0x02 : 0x00) | temp;
			return; case 10: // lsr
				temp = (cpu->a & 0x01);
				cpu->a = cpu->a >> 1;
				cpu->flags = (cpu->flags & 0x7c) | (cpu->a & 0x80) | ((cpu->a == 0) ? 0x02 : 0x00) | temp;
			return; case 11:  // ror
				temp = (cpu->a & 0x01);
				cpu->a = (cpu->a >> 1) | ((cpu->flags & 1) << 7);
				cpu->flags = (cpu->flags & 0x7c) | (cpu->a & 0x80) | ((cpu->a == 0) ? 0x02 : 0x00) | temp;
			return; case 12:  // txa
				data = cpu->a = cpu->x;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 13: // tax
				data = cpu->x = cpu->a;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 14: // dex
				data = cpu->x -= 1;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 15: // nop
				;
			return; case 16: // clc
				cpu->flags &= ~0x01;
			return; case 17: // sec
				cpu->flags |= 0x01;
			return; case 18: // cli
				cpu->flags &= ~0x04;
			return; case 19: // sei
				cpu->flags |= 0x04;
			return; case 20: // tya
				data = cpu->a = cpu->y;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 21: // clv
				cpu->flags &= ~0x40;
			return; case 22: // cld
				cpu->flags &= ~0x08;
			return; case 23: // sed
				cpu->flags |= 0x20; // debug mode flag
			return; case 24: // inc
				data = cpu->a += 1;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 25: // dec
				data = cpu->a -= 1;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 26: // phy
				io(this,  0x0100 + cpu->sp--, cpu->y, 0x1);
			return; case 27: // ply
				data = cpu->y = io(this, ++cpu->sp + 0x0100, 0, 0x0);
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 28: // txs
				cpu->sp = cpu->x;
			return; case 29: // tsx
				data = cpu->x = cpu->sp;
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			return; case 30: // phx
				io(this,  0x0100 + cpu->sp--, cpu->x, 0x1);
			return; case 31: // plx
				data = cpu->x = io(this, ++cpu->sp + 0x0100, 0, 0x0);
				cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
			}
		}
		if ((opcode & 0x1f) == 0x10) { // branches
			const u8 b[4] = {1<<7, 1<<6, 1<<0, 1<<1};
			if ((cpu->flags ^ ((opcode & 0x20) ? 0x00 : 0xff)) & b[(opcode & 0xc0) >> 6]) {
				cpu->pc = cpu->pc + (s8)(oparand & 0xff);
			}
			return;
		}
		if (opcode == 0x20) { // jsr a
			cpu->pc--;
			io(this,  0x0100 + cpu->sp--, ((cpu->pc) & 0xff00) >> 8, 0x1);
			io(this,  0x0100 + cpu->sp--, (cpu->pc) & 0xff, 0x1);
			cpu->pc = oparand;
			return;
		}
		if (opcode == 0x40) { // rti
			cpu->flags = (cpu->flags & 0x30) | (io(this, ++cpu->sp + 0x0100, 0, 0x0) & 0xcf);
			oparand = io(this, ++cpu->sp + 0x0100, 0, 0x0) << 0;
			oparand |= io(this, ++cpu->sp + 0x0100, 0, 0x0) << 8;
			cpu->pc = oparand;
			return;
		}
		if (opcode == 0x60) { // rts
			oparand = io(this, ++cpu->sp + 0x0100, 0, 0x0) << 0;
			oparand |= io(this, ++cpu->sp + 0x0100, 0, 0x0) << 8;
			cpu->pc = oparand + 1;
			return;
		}
		if (opcode == 0x80) { // bra
			cpu->pc = cpu->pc + (s8)(oparand&0xff);
			return;
		}

		// else continue as a ordinary instructions
		if (addressing_mode == 0) {
			addressing_mode = 2;  // top row is immediate addressing mode
			if (oparation > 8 && oparation != 13) oparation = 2;  // NOP
		} else {
			addressing_mode = 8; // 65C02 indirect addressing mode
			oparation += 8;
		}
	}

	if (opcode == 0x4c) { // jmp a
		cpu->pc = oparand;
		return;
	}
	if (opcode == 0x6c) { // jmp (a)
		u16 temp = io(this, oparand, 0, 0x0) << 0;
		temp |= io(this, oparand + 1, 0, 0x0) << 8;
		cpu->pc = temp;
		return;
	}
	if (opcode == 0x7c) { // jmp (a,x)
		u16 temp = io(this, oparand + cpu->x, 0, 0x0) << 0;
		temp |= io(this, oparand + cpu->x + 1, 0, 0x0) << 8;
		cpu->pc = temp;
		return;
	}
	if (opcode == 0x14 || opcode == 0x1c) { // trb
		data = io(this, oparand, 0, 0x0);
		cpu->flags = (cpu->flags & 0xfd) | (((data & cpu->a) == 0) ? 0x02 : 0x00);
		data = data & ~cpu->a;
		io(this, oparand, data, 0x1);
		return;
	}

	int temp;

	// misc fixups before going into the main operation
	if (opcode == 0x89) oparation = 1;  // BIT #i
	if (opcode == 0x9c) oparation = addressing_mode = 3;  // STZ a
	if (opcode == 0x9e) oparation = 3;  // STZ a,x
	if ((oparation == 7) || (oparation == 12) || (oparation == 13)) {
		if (addressing_mode == 5) addressing_mode = 9;  // if X is a oparand, index with Y
		if (addressing_mode == 7) addressing_mode = 6;
	}

	const u8 op_rw[32] = {
		3, 1, 1, 2, 2, 1, 1, 1, 3, 3, 3, 3, 2, 1, 3, 3,
		1, 1, 1, 1, 2, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1};
	u32 addr = 0;
	switch (addressing_mode) {
	case 0:         // (d,x)
		oparand = (oparand + cpu->x) & 0xff;
		addr = io(this, oparand, 0, 0x0) << 0;
		addr |= io(this, oparand + 1, 0, 0x0) << 8;
	break; case 1:  // d
		addr = oparand & 0xff;
	break; case 2:  // #i
		addr = cpu->pc - 1;
		data = oparand & 0xff;
	break; case 3:  // a
		addr = oparand;
	break; case 4:  // (d),y
		oparand = oparand & 0xff;
		addr = io(this, oparand, 0, 0x0) << 0;
		addr |= io(this, oparand + 1, 0, 0x0) << 8;
		addr += cpu->y;
	break; case 5:  // d,x
		addr = (oparand + cpu->x) & 0xff;
	break; case 6:  // a,y
		addr = oparand + cpu->y;
	break; case 7:  // a,x
		addr = oparand + cpu->x;
	break; case 8:  // (a)
		addr = io(this, oparand, 0, 0x0) << 0;
		addr |= io(this, oparand + 1, 0, 0x0) << 8;
	break; case 9:  // d,y
		addr = oparand + cpu->y;
	break;
	}

	if ((op_rw[oparation] & 1) && addressing_mode != 2) data = io(this, addr, 0, (op_rw[oparation] & 2) ? 0x8 : 0x0);

	switch (oparation) {
	case 0:         // tsb
		cpu->flags = (cpu->flags & 0xfd) | (((data & cpu->a) == 0) ? 0x02 : 0x00);
		data = data | cpu->a;
	break; case 1:  // bit
		cpu->flags = (cpu->flags & 0x3d) | (data & 0xc0) | (((data & cpu->a) == 0) ? 0x02 : 0x00);
	break; case 2:  // nop
	break; case 3:  // stz
		data = 0;
	break; case 4: // sty
		data = cpu->y;
	break; case 5: // ldy
		cpu->y = data;
		cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
	break; case 6: // cpy
		temp = cpu->y - data;
		data = temp;
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | ((temp >= 0) ? 0x01 : 0x00);
	break; case 7: // cpx
		temp = cpu->x - data;
		data = temp;
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | ((temp >= 0) ? 0x01 : 0x00);
	break; case 8:  // asl
		temp = (data & 0x80) >> 7;
		data = data << 1;
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | temp;
	break; case 9:  // rol
		temp = (data & 0x80) >> 7;
		data = (data<<1) | (cpu->flags & 1);
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | temp;
	break; case 10: // lsr
		temp = (data & 0x01);
		data = data >> 1;
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | temp;
	break; case 11: // ror
		temp = (data & 0x01);
		data = (data>>1) | ((cpu->flags & 1) << 7);
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | temp;
	break; case 12: // stx
		data = cpu->x;
	break; case 13: // ldx
		cpu->x = data;
		cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
	break; case 14: // dec
		data -= 1;
		cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
	break; case 15: // inc
		data += 1;
		cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
	break; case 16:  // ora
		data = cpu->a | data;
		cpu->a = data;
		cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
	break; case 17:  // and
		data = cpu->a & data;
		cpu->a = data;
		cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
	break; case 18:  // eor
		data = cpu->a ^ data;
		cpu->a = data;
		cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
	break; case 19: // adc
		temp = cpu->a + data + (cpu->flags & 1);
		cpu->flags = (cpu->flags & 0xbf) | (((temp & 0x0100) >> 2) ^ ((temp & 0x0080) >> 1) ^ ((cpu->a & 0x80) >> 1) ^ ((data & 0x80) >> 1));
		data = temp;
		cpu->a = data;
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | ((temp & 0x0100) >> 8);
	break; case 20: // sta
		data = cpu->a;
		if (ill_block) data = cpu->a & cpu->x;  // sax
	break; case 21: // lda
		cpu->a = data;
		cpu->flags = (cpu->flags & 0x7d) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00);
		if (ill_block) cpu->x = data;  // lax
	break; case 22: // cmp
		temp = cpu->a - data;
		data = temp;
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | ((temp >= 0) ? 0x01 : 0x00);
	break; case 23: // sbc
		temp = cpu->a + (data^0xff) + (cpu->flags & 1);
		cpu->flags = (cpu->flags & 0xbf) | (((temp & 0x0100) >> 2) ^ ((temp & 0x0080) >> 1) ^ ((cpu->a & 0x80) >> 1) ^ (((data^0xff) & 0x80) >> 1));
		data = temp;
		cpu->a = data;
		cpu->flags = (cpu->flags & 0x7c) | (data & 0x80) | ((data == 0) ? 0x02 : 0x00) | ((temp & 0x0100) >> 8);
	break;
	}
	if ((op_rw[oparation] & 2) && addressing_mode != 2) io(this, addr, data, (op_rw[oparation] & 1) ? 0x9 : 0x1);
	return;
}

void retro_imitator_advance_frame (retro_imitator_state* this, u16 p1, u16 p2, bool reset_request, u8 vid_opt, u8 snd_opt)
{
	if (!this || this->init_magic != RETRO_IMITATOR_INIT_MAGIC) return;
	u16 m;
	m = p1;
	m = m & (m >> 1) & 0x50;
	m = m | (m << 1);
	this->p1_in = ((p1 & ~m) | (this->p1_in & m));
	m = p2;
	m = m & (m >> 1) & 0x50;
	m = m | (m << 1);
	this->p2_in = ((p2 & ~m) | (this->p2_in & m));
	this->render_option = vid_opt;
	this->audio_chn_mute = snd_opt;

	if (reset_request) {
		this->cpu.irq |= 1;
		this->cycles = 0;
	}
	if (this->vid_mode & 0x0080) this->cpu.irq |= 2;
	if (!(this->vid_mode & 0x0080) && !(this->audio_chn_enable & 0xc0)) this->cpu.irq |= 4;

	bool mode12_save_previous_ppu_state = false;
	u16 previous_chr_bank[16] = {0};
	u16 previous_vid_mode = 0;
	u8 previous_vid_sprite_page = 0;
	u8 previous_vid_scroll_x = 0;
	u8 previous_vid_scroll_y = 0;

	bool pcm_frame = false;
	// 22050 cycles for the first 10 frames for region detection (at least nmi based)
	u32 begin_cycles = (this->cycles & ~0xff);
	u32 target_cycles = 0x010000;
	if (this->cycles < 0x5600*10) {
		target_cycles += 0x5600 - 0x010000;
		pcm_frame = true;
	}
	while (this->cycles < begin_cycles + target_cycles) {
		retro_imitator_cpu_next(this);
		this->cpu.pc &= 0xffffff;
		// if we're gonna play pcm, slow down the rate to be not so high pitch
		if (!pcm_frame && this->raw_pcm_len >= 4) {
			target_cycles += 0x5600 - 0x010000;
			pcm_frame = true;
		}
		if ((this->render_option & (1<<5)) && this->mode12_irq && (this->cycles > begin_cycles + target_cycles*3/4)) {
			this->cpu.irq |= 4;
			if (!mode12_save_previous_ppu_state) {
				for (int i = 0; i < 16; ++i) previous_chr_bank[i] = this->chr_bank[i];
				previous_vid_mode = this->vid_mode;
				previous_vid_sprite_page = this->vid_sprite_page;
				previous_vid_scroll_x = this->vid_scroll_x;
				previous_vid_scroll_y = this->vid_scroll_y;
				mode12_save_previous_ppu_state = true;
			}
		}
	}
	if (this->cycles >= 0x5600*10) this->cycles = (this->cycles & 0xff) + 0x5600*10;
	if (mode12_save_previous_ppu_state) {
		for (int i = 0; i < 16; ++i) this->chr_bank[i] = previous_chr_bank[i];
		this->vid_mode = previous_vid_mode;
		this->vid_sprite_page = previous_vid_sprite_page;
		this->vid_scroll_x = previous_vid_scroll_x;
		this->vid_scroll_y = previous_vid_scroll_y;
	}
}

static bool retro_imitator_plausible_vectors (u8* data, size_t data_size)
{
	if (data_size < 16) return false;
	int NMI = ((int)(data[data_size-6] & 0xff) | (int)(data[data_size-5] & 0xff) << 8);
	int RST = ((int)(data[data_size-4] & 0xff) | (int)(data[data_size-3] & 0xff) << 8);
	int IRQ = ((int)(data[data_size-2] & 0xff) | (int)(data[data_size-1] & 0xff) << 8);
	if ((RST < 0x8000) || (0xfffa <= RST) || (RST == NMI) ||
		(0x0800 <= NMI && NMI < 0x4020) || (0xfffa <= NMI) ||
		(0x1000 <= IRQ && IRQ < 0x4020) || (0xfffa <= IRQ && IRQ < 0xffff)) {
		return false;
	}

	const u8 plausible_first_opcode[] = {
		0x09, 0x18, 0x20, 0x2c, 0x38, 0x4c, 0x58, 0x78,
		0x84, 0x85, 0x86, 0x8c, 0x8d, 0x8e, 0xa0, 0xa2,
		0xa9, 0xac, 0xad, 0xae, 0xb8, 0xba, 0xc6, 0xd8,
		0xe6, 0xea, 0xee};

	size_t index = data_size - ((0x10000 - RST) % data_size);
	if (index == data_size) index = 0;
	u8 opcode = data[index];
	for (size_t x = 0; x < sizeof(plausible_first_opcode); ++x) {
		if (plausible_first_opcode[x] == opcode) return true;
	}
	return false;
}

static int retro_imitator_parse_prg_chr_breakpoint (u8* data, int data_size, int* offset_out)
{
	// start with assuming PRG is the largest power of 2
	int prg_size = retro_imitator_round_pot(data_size);
	if (data_size < prg_size) prg_size = prg_size >> 1;
	// and CHR is the largest power of 2 of any remainder.
	int chr_size = retro_imitator_round_pot(data_size - prg_size);
	if (data_size < prg_size + chr_size) chr_size = chr_size >> 1;
	if (chr_size < 1024) chr_size = 0;  // minimum size of CHR ROM
	// align segment windows to the end of the data, to discard junk header data
	int offset = data_size - (prg_size + chr_size);
	if (offset_out) *offset_out = offset;
	int total_size = data_size - offset;

	const u32 like_prg[8] = {
		0x2efe2e74, 0x047e7fef, 0x026e7f6a, 0x00242736,
		0x2fff7f74, 0x227f7fff, 0x000f2720, 0x00000720
	};
	const u32 like_chr[8] = {
		0xd101d188, 0xd1010000, 0x80000005, 0xf9818048,
		0x80008003, 0xc0000000, 0x980090cb, 0x7fcbc08b
	};
	// check each combination of power of 2 and
	// choose the first configuration that seems plausible
	int return_breakpoint = offset + prg_size;
	int return_score = INT_MIN;
	for ( ; 4096 <= prg_size; prg_size = prg_size >> 1) {
		chr_size = retro_imitator_round_pot(total_size - prg_size);
		if (chr_size != total_size - prg_size) continue;
		if (retro_imitator_plausible_vectors(&data[offset], prg_size)) return offset+prg_size;
		int score = 0;
		for (int i = 0; i < prg_size; ++i) {
			u8 c = data[offset + i];
			score += ((like_prg[c/32] & (1<<(c%32))) ? 1 : 0);
			score -= ((like_chr[c/32] & (1<<(c%32))) ? 1 : 0);
		}
		for (int i = 0; i < chr_size; ++i) {
			u8 c = data[offset + prg_size + i];
			score -= ((like_prg[c/32] & (1<<(c%32))) ? 1 : 0);
			score += ((like_chr[c/32] & (1<<(c%32))) ? 1 : 0);
		}
		if (return_score < score) {
			return_breakpoint = offset + prg_size;
			return_score = score;
		}
	};
	return return_breakpoint;
}

#ifdef __EMSCRIPTEN__
const u8 retro_imitator_palette[64] = "H45:98;7GCDEIB33]F6>=<?K[WSTU333rVJNRQP_okchfH33rjZ^ba`pqlimn]33";
#else
const u8 __attribute__ ((nonstring)) retro_imitator_palette[64] = "H45:98;7GCDEIB33]F6>=<?K[WSTU333rVJNRQP_okchfH33rjZ^ba`pqlimn]33";
#endif

static u8 retro_imitator_get_bg_color(retro_imitator_state* this) {
	u8 bg_c = 0;
	u8 inx = this->bg_index & 0x10;
	if (!(this->vid_mode & 0x0100)) {
		bg_c = this->mem[0x0fe0 + inx] & 0x3f;
		if (!(this->vid_mode & 0x0040)) bg_c = retro_imitator_palette[bg_c] - '3';
	}
	return bg_c;
}

void retro_imitator_render(retro_imitator_state* this, u8* buffer, int pitch, int buf_w, int buf_h, int off_x, int off_y)
{
	if (!this || this->init_magic != RETRO_IMITATOR_INIT_MAGIC) return;
	u8 bg_index = this->bg_index & 0x10;
	for (int i = 0; i < buf_h*pitch; ++i) buffer[i] = bg_index | 0x20;
	for (int y = off_y; y < off_y+240; ++y) {
		if (buf_h <= y) break;
		for (int x = off_x; x < off_x+256; ++x) {
			if (buf_w <= x) break;
			buffer[y*pitch + x] = bg_index;
		}
	}
	// buffer pixels; bit 4~0: index into palette, bit 5: overscan, bit 6: sprite, bit 7: bg/fg

	int scroll_x = this->vid_scroll_x | (this->vid_mode & 0x0001) << 8;
	int scroll_y = this->vid_scroll_y | (this->vid_mode & 0x0002) << 7;
	if (240 <= (this->vid_scroll_y & 0xff)) scroll_y -= 256;
	if ((this->vid_mode & 0x0002)) scroll_y -= 16;
	if (scroll_y < 0) scroll_y += 480;

	int y_size = (this->vid_mode & 0x20) ? 16 : 8;
	for (int n = 63; n >= 0; --n) {
		u8* sprite_ptr = &this->mem[0x0e00];
		if (this->vid_sprite_page < 0x60) sprite_ptr = &this->mem[(u16)this->vid_sprite_page << 8];
		u16 y = sprite_ptr[(n*4 + 0) & 0xff];
		u16 i = sprite_ptr[(n*4 + 1) & 0xff] + ((this->vid_mode & 0x08) ? 0x0100 : 0);
		u8 a = sprite_ptr[(n*4 + 2) & 0xff];
		u8 x = sprite_ptr[(n*4 + 3) & 0xff];

		if (y_size > 8) i = (i & 0xfe) | ((i & 0x01) << 8);

		u8* tile_gfx = &(this->mem[0x1000 + ((i*16) & 0x1fff)]);
		if (512 <= this->chr_size) {
			int chr_mask = retro_imitator_round_pot(this->chr_size) - 1;
			int tlb_slot = i/32;
			int b = ((this->chr_seg_conf >> (tlb_slot*2)) & 0x03) + 1;
			if (this->chr_seg_conf == 0xedec1213) b = 0;
			tlb_slot = tlb_slot & ~(0x0f >> b);
			int chr_bank = this->chr_bank[tlb_slot];
			tile_gfx = &(this->chr[(chr_bank*(0x2000>>b) + (i & (0x01ff>>b))*16) & chr_mask]);
		}
		for (int py = 0; py < y_size; ++py) {
			int sliver_offset = (a & 0x80) ? (y_size-1)-py : py;
			if (sliver_offset >= 8) sliver_offset += 8;
			u8 sliver_a = tile_gfx[sliver_offset + 0];
			u8 sliver_b = tile_gfx[sliver_offset + 8];
			if ((sliver_a | sliver_b) == 0x00) continue;
			int sy = (y+py);
			sy += off_y;
			if (sy >= buf_h) continue;
			for (int px = 0; px < 8; ++px) {
				int sx = (a & 0x40) ? (x+(7-px)) : (x+px);
				sx += off_x;
				int c = ((sliver_a >> (7-px)) & 0x01) | (((sliver_b >> (7-px)) & 0x01) << 1);
				if (c) {
					u8 pix = c | ((a & 0x03) << 2) | 0x10 | ((~a & 0x20) << 2) | 0x40;
					if (sx < buf_w) buffer[sy*pitch + sx] = pix;
					if (this->render_option & (1<<4)) {
						if (sx+256 < buf_w) buffer[sy*pitch + sx+256] = pix;
						if (sx-256 >= 0) buffer[sy*pitch + sx-256] = pix;
					}
				}
			}
		}
	}

	for (int map_addr = 0x3000; map_addr < 0x4000; map_addr += 0x0400) {
		for (int ty = 0; ty < 30; ++ty) {
			for (int tx = 0; tx < 32; ++tx) {
				u8 a = this->mem[map_addr + 0x03c0 + ((ty>>2)*8) + (tx>>2)];
				a = (a >> ((((tx&2)>>1)*2) + (((ty&2)>>1)*4))) & 0x03;
				u16 i = this->mem[map_addr + ty*32 + tx] + ((this->vid_mode & 0x10) ? 0x0100 : 0);

				u8* tile_gfx = &(this->mem[0x1000 + ((i*16) & 0x1fff)]);
				if (512 <= this->chr_size) {
					int chr_mask = retro_imitator_round_pot(this->chr_size) - 1;
					int tlb_slot = i/32;
					int b = ((this->chr_seg_conf >> (tlb_slot*2)) & 0x03) + 1;
					if (this->chr_seg_conf == 0xedec1213) b = 0;
					tlb_slot = tlb_slot & ~(0x0f >> b);
					int chr_bank = this->chr_bank[tlb_slot];
					tile_gfx = &(this->chr[(chr_bank*(0x2000>>b) + (i & (0x01ff>>b))*16) & chr_mask]);
				}
				for (int py = 0; py < 8; ++py) {
					u8 sliver_a = tile_gfx[py + 0];
					u8 sliver_b = tile_gfx[py + 8];
					if ((sliver_a | sliver_b) == 0x00) continue;
					int sy = ((map_addr & 0x0800) ? 240 : 0) + ty*8 + py;
					sy = (sy + 480 - scroll_y + off_y) % 480;
					for (int px = 0; px < 8; ++px) {
						int sx = ((map_addr & 0x0400) ? 256 : 0) + tx*8 + px;
						sx = (sx + 512 - scroll_x + off_x) & 0x01ff;
						if (sx >= buf_w || sy >= buf_h) continue;
						int c = ((sliver_a >> (7-px)) & 0x01) | (((sliver_b >> (7-px)) & 0x01) << 1);
						u8 b = buffer[sy*pitch + sx];
						if (c && !(b & 0x80)) buffer[sy*pitch + sx] = c | (a << 2) | 0x80 | (b & 0x20);
					}
				}
			}
		}
	}

	u8 bg_c = retro_imitator_get_bg_color(this);
	for (int y = buf_h-1; y >= 0; --y) {
		for (int x = buf_w-1; x >= 0; --x) {
			int i = y*pitch + x;
			u8 inx = buffer[i] & 0x1f;

			u8 c = (inx & 0x03) * 0x15;
			if (!(this->vid_mode & 0x0100)) {
				c = this->mem[0x0fe0 + inx] & 0x3f;
				if (!(this->vid_mode & 0x0040)) c = retro_imitator_palette[c] - '3';
			}
			if (!(this->render_option & (1<<3))) {
				u8 d0 = buffer[i] & 0xc0;
				u8 d3 = ((x >= 3) && (y >= 2)) ? (buffer[(y-2)*pitch + (x-3)] & 0xc0) : 0x40;
				u8 d1 = ((x >= 1) && (y >= 1)) ? (buffer[(y-1)*pitch + (x-1)] & 0xc0) : 0x40;
				if (d0 < d3) {
					c |= 0x40;
				} else if (d0 > d1) {
					c |= 0x80;
				}
				if (buffer[i] & 0x20) c |= 0xc0;
			}
			if ((this->render_option & (1<<3)) && (buffer[i] & 0x20)) c = bg_c;

			buffer[i] = c;
		}
	}
}

// https://justine.lol/endian.html
#define WRITE16LE(P, V)                        \
  ((P)[0] = (0x00000000000000FF & (V)) >> 000, \
   (P)[1] = (0x000000000000FF00 & (V)) >> 010, (P) + 2)
#define WRITE32LE(P, V)                        \
  ((P)[0] = (0x00000000000000FF & (V)) >> 000, \
   (P)[1] = (0x000000000000FF00 & (V)) >> 010, \
   (P)[2] = (0x0000000000FF0000 & (V)) >> 020, \
   (P)[3] = (0x00000000FF000000 & (V)) >> 030, (P) + 4)
#define WRITE64LE(P, V)                        \
  ((P)[0] = (0x00000000000000FF & (V)) >> 000, \
   (P)[1] = (0x000000000000FF00 & (V)) >> 010, \
   (P)[2] = (0x0000000000FF0000 & (V)) >> 020, \
   (P)[3] = (0x00000000FF000000 & (V)) >> 030, \
   (P)[4] = (0x000000FF00000000 & (V)) >> 040, \
   (P)[5] = (0x0000FF0000000000 & (V)) >> 050, \
   (P)[6] = (0x00FF000000000000 & (V)) >> 060, \
   (P)[7] = (0xFF00000000000000 & (V)) >> 070, (P) + 8)

#define READ16LE(S) ((255 & (S)[1]) << 8 | (255 & (S)[0]))
#define READ32LE(S)                                                    \
  ((u32)(255 & (S)[3]) << 030 | (u32)(255 & (S)[2]) << 020 | \
   (u32)(255 & (S)[1]) << 010 | (u32)(255 & (S)[0]) << 000)
#define READ64LE(S)                                                    \
  ((u64)(255 & (S)[7]) << 070 | (u64)(255 & (S)[6]) << 060 | \
   (u64)(255 & (S)[5]) << 050 | (u64)(255 & (S)[4]) << 040 | \
   (u64)(255 & (S)[3]) << 030 | (u64)(255 & (S)[2]) << 020 | \
   (u64)(255 & (S)[1]) << 010 | (u64)(255 & (S)[0]) << 000)

bool retro_imitator_state_save(retro_imitator_state* this)
{
	if (!this || this->init_magic != RETRO_IMITATOR_INIT_MAGIC) return false;
	assert(sizeof(double) == sizeof(u64));
	u8* p = &(this->mem[0x0e00]);

	p = WRITE32LE(p, this->init_magic);
	p = WRITE32LE(p, this->cycles);
// cpu
	p = WRITE16LE(p, this->cpu.pc);
	*p++ = this->cpu.flags;
	*p++ = this->cpu.y;
	*p++ = this->cpu.a;
	*p++ = this->cpu.x;
	*p++ = this->cpu.sp;
	*p++ = this->cpu.irq;
// memory mapping
	p = WRITE32LE(p, this->chr_seg_conf);
	p = WRITE16LE(p, this->prg_seg_conf);
	p = WRITE16LE(p, this->prg_mode);
	p = WRITE16LE(p, this->mode9_addr_check);
	*p++ = this->mode9_shift;
	*p++ = this->mode12_irq ? 1 : 0;
	*p++ = this->mode20_reg;
	*p++ = (this->cpu.pc & 0xff0000) >> 16;
	*p++ = this->render_option;
	*p++ = this->audio_chn_mute;
	for (int i = 0; i < 4; ++i) *p++ = this->mode4_reg[i];
	for (int i = 0; i < 4; ++i) *p++ = this->mode9_reg[i];
	for (int i = 0; i < 4; ++i) *p++ = this->mode12_reg[i];
	for (int i = 0; i < 4; ++i) *p++ = 0;
// memory banks
	for (int i = 0; i < 8; ++i) p = WRITE16LE(p, this->prg_bank[i]);
	for (int i = 0; i < 16; ++i) p = WRITE16LE(p, this->chr_bank[i]);
// gamepad
	p = WRITE16LE(p, this->p1_in);
	p = WRITE16LE(p, this->p2_in);
	p = WRITE16LE(p, this->p1);
	p = WRITE16LE(p, this->p2);
	for (int i = 0; i < 8; ++i) *p++ = 0;
// video
	p = WRITE16LE(p, this->vid_mode);
	p = WRITE16LE(p, this->vid_tile_ptr);
	*p++ = this->vid_sprite_page;
	*p++ = this->vid_sprite_ptr;
	*p++ = this->vid_scroll_x;
	*p++ = this->vid_scroll_y;
	*p++ = this->bg_index;
	*p++ = this->vsync_state;
	*p++ = this->nt_mir;
	*p++ = this->nt_mir_prev_byte;
	for (int i = 0; i < 4; ++i) *p++ = 0;
// audio
	for (int i = 0; i < 8; ++i) p = WRITE32LE(p, this->audio_chn_info[i]);
	for (int i = 0; i < 4; ++i) p = WRITE16LE(p, this->audio_chn_length[i]);
	*p++ = this->audio_chn_enable;
	*p++ = this->audio_hi_carry ? 1 : 0;
	*p++ = this->pwm_triggered ? 1 : 0;
	*p++ = this->pwm_rate;
	p = WRITE16LE(p, this->pwm_len);
	p = WRITE16LE(p, this->pwm_addr);
	p = WRITE32LE(p, this->pwm_playhead);
	p = WRITE16LE(p, this->raw_pcm_len);
	*p++ = this->raw_pcm[0];
	for (int i = 0; i < 9; ++i) *p++ = 0;
// pad to 256 bytes
	for (int i = 0; i < 64; ++i) *p++ = 0;
// audio gen
	u64 val;
	for (int i = 0; i < 7; ++i) {
		memcpy(&val, &(this->audio_chn_phase[i]), sizeof(double));
		p = WRITE64LE(p, val);
	}
	p = WRITE64LE(p, this->snd_gen_rand);
	memcpy(&val, &(this->x1), sizeof(double));
	p = WRITE64LE(p, val);
	memcpy(&val, &(this->x2), sizeof(double));
	p = WRITE64LE(p, val);
	memcpy(&val, &(this->x3), sizeof(double));
	p = WRITE64LE(p, val);
	memcpy(&val, &(this->y1), sizeof(double));
	p = WRITE64LE(p, val);
	memcpy(&val, &(this->y2), sizeof(double));
	p = WRITE64LE(p, val);
	memcpy(&val, &(this->y3), sizeof(double));
	p = WRITE64LE(p, val);
	memcpy(&val, &(this->high_pass_filter_x), sizeof(double));
	p = WRITE64LE(p, val);
	memcpy(&val, &(this->high_pass_filter_y), sizeof(double));
	p = WRITE64LE(p, val);

	assert((size_t)(p - &(this->mem[0x0e00])) == 384);
	return true;
}

bool retro_imitator_state_restore(retro_imitator_state* this)
{
	if (!this) return false;
	assert(sizeof(double) == sizeof(u64));
	const u8* p = &(this->mem[0x0e00]);

	u32 init_magic = READ32LE(p); p += 4;
	if (init_magic != RETRO_IMITATOR_INIT_MAGIC) return false;

	this->init_magic = init_magic;
	this->cycles = READ32LE(p); p += 4;
// cpu
	this->cpu.pc = READ16LE(p); p += 2;
	this->cpu.flags = *p++;
	this->cpu.y = *p++;
	this->cpu.a = *p++;
	this->cpu.x = *p++;
	this->cpu.sp = *p++;
	this->cpu.irq = *p++;
// memory mapping
	this->chr_seg_conf = READ32LE(p); p += 4;
	this->prg_seg_conf = READ16LE(p); p += 2;
	this->prg_mode = READ16LE(p); p += 2;
	this->mode9_addr_check = READ16LE(p); p += 2;
	this->mode9_shift = *p++;
	this->mode12_irq = (*p++) ? true : false;;
	this->mode20_reg = *p++;
	this->cpu.pc = this->cpu.pc | ((*p++) << 16);
	this->render_option = *p++;
	this->audio_chn_mute = *p++;
	for (int i = 0; i < 4; ++i) this->mode4_reg[i] = *p++;
	for (int i = 0; i < 4; ++i) this->mode9_reg[i] = *p++;
	for (int i = 0; i < 4; ++i) this->mode12_reg[i] = *p++;
	p += 4;
// memory banks
	for (int i = 0; i < 8; ++i) {this->prg_bank[i] = READ16LE(p); p += 2;}
	for (int i = 0; i < 16; ++i) {this->chr_bank[i] = READ16LE(p); p += 2;}
// gamepad
	this->p1_in = READ16LE(p); p += 2;
	this->p2_in = READ16LE(p); p += 2;
	this->p1 = READ16LE(p); p += 2;
	this->p2 = READ16LE(p); p += 2;
	p += 8;
// video
	this->vid_mode = READ16LE(p); p += 2;
	this->vid_tile_ptr = READ16LE(p); p += 2;
	this->vid_sprite_page = *p++;
	this->vid_sprite_ptr = *p++;
	this->vid_scroll_x = *p++;
	this->vid_scroll_y = *p++;
	this->bg_index = *p++;
	this->vsync_state = *p++;
	this->nt_mir = *p++;
	this->nt_mir_prev_byte = *p++;
	p += 4;
// audio
	for (int i = 0; i < 8; ++i) {this->audio_chn_info[i] = READ32LE(p); p += 4;}
	for (int i = 0; i < 4; ++i) {this->audio_chn_length[i] = READ16LE(p); p += 2;}
	this->audio_chn_enable = *p++;
	this->audio_hi_carry = (*p++) ? true : false;
	this->pwm_triggered = (*p++) ? true : false;
	this->pwm_rate = *p++;
	this->pwm_len = READ16LE(p); p += 2;
	this->pwm_addr = READ16LE(p); p += 2;
	this->pwm_playhead = READ32LE(p); p += 4;
	this->raw_pcm_len = READ16LE(p); p += 2;
	this->raw_pcm[0] = *p++;
	p += 9;
// pad to 256 bytes
	p += 64;
// audio gen
	u64 val;
	for (int i = 0; i < 7; ++i) {
		val = READ64LE(p); p += 8;
		memcpy(&(this->audio_chn_phase[i]), &val, sizeof(double));
	}
	this->snd_gen_rand = READ64LE(p); p += 8;
	val = READ64LE(p); p += 8;
	memcpy(&(this->x1), &val, sizeof(double));
	val = READ64LE(p); p += 8;
	memcpy(&(this->x2), &val, sizeof(double));
	val = READ64LE(p); p += 8;
	memcpy(&(this->x3), &val, sizeof(double));
	val = READ64LE(p); p += 8;
	memcpy(&(this->y1), &val, sizeof(double));
	val = READ64LE(p); p += 8;
	memcpy(&(this->y2), &val, sizeof(double));
	val = READ64LE(p); p += 8;
	memcpy(&(this->y3), &val, sizeof(double));
	val = READ64LE(p); p += 8;
	memcpy(&(this->high_pass_filter_x), &val, sizeof(double));
	val = READ64LE(p); p += 8;
	memcpy(&(this->high_pass_filter_y), &val, sizeof(double));

	assert((size_t)(p - &(this->mem[0x0e00])) == 384);
	return true;
}

/* TODO: new diff format:
ctrl byte be 2 nibbles for length of literals and length of run.
like lz4 when either is 0xf read bytes for length of each
*/

size_t retro_imitator_state_make_diff(u8 patch[0x8000+0x4000], u16 data_size, const u8 old[data_size], const u8 new[data_size])
{
	size_t l = 0;
	int skip_len = 0;
	for (size_t i = 0; i < data_size; ++i) {
		u8 c = old[i] ^ new[i];
		if (!c) {
			skip_len += 1;
			if (i+1 < data_size) continue;
		}
		if (skip_len) {
			skip_len -= 1;
			patch[l++] = 0x00;
			if (skip_len >= 0x80) patch[l++] = ((skip_len & 0x7f00) | 0x8000) >> 8;
			patch[l++] = (skip_len & 0x00ff);
			skip_len = 0;
		}
		if (c) patch[l++] = c;
	}
	return l;
}

size_t retro_imitator_state_apply_diff(u16 data_size, u8 data[data_size], const u8 patch[0x8000+0x4000])
{
	size_t l = 0;
	for (size_t i = 0; i < data_size; ++i) {
		u8 byte = patch[l++];
		if (byte) {
			data[i] ^= byte;
			continue;
		}
		int skip_len = patch[l++];
		if (skip_len & 0x80) skip_len = ((skip_len << 8) | patch[l++]) & 0x7fff;
		i += skip_len;
	}
	return l;
}

void retro_imitator_init(retro_imitator_state* this, u8 *rom, size_t rom_size)
{
	if (!this) return;
	for(size_t i = 0; i < sizeof(retro_imitator_state); ++i) ((u8*)this)[i] = 0;

	u8 *prg = NULL;
	size_t prg_size = 0;
	u8 *chr = NULL;
	size_t chr_size = 0;
	int prg_offset = 0;
	int chr_offset = retro_imitator_parse_prg_chr_breakpoint(rom, rom_size, &prg_offset);
	prg = &rom[prg_offset];
	prg_size = chr_offset - prg_offset;
	if (rom_size - chr_offset > 0) {
		chr = &rom[chr_offset];
		chr_size = rom_size - chr_offset;
	}
	this->init_magic = RETRO_IMITATOR_INIT_MAGIC;

	this->prg = prg;
	this->prg_size = prg_size;
	if (8388608 < this->prg_size) this->prg_size = 8388608; // max size is 256 * 32K banks
	this->chr = chr;
	this->chr_size = chr_size;
	if (2097152 < this->chr_size) this->chr_size = 2097152; // max size is 256 * 8K banks

	this->prg_seg_conf = 0x0000;
	this->chr_seg_conf = 0xedec1213;
	for (int i = 0; i < 8; ++i) this->prg_bank[i] = 0xffff;
	for (int i = 0; i < 16; ++i) this->chr_bank[i] = 0;
	this->prg_mode = 0;  // 8k CHR
	if (32768 < prg_size) {
		this->prg_mode = 2;  // 16k PRG
		bool is_32k_banks = true;
		for (size_t i = 0; i < (prg_size/32768); ++i) {
			bool plausible = retro_imitator_plausible_vectors(&prg[i*32768], 32768);
			if (plausible) {
				this->prg_bank[0] = i;
			} else {
				is_32k_banks = false;
			}
		}
		if (is_32k_banks) this->prg_mode = 3; // 32k PRG
		if (8192 < chr_size) this->prg_mode = 1;   // 32k PRG low nibble, 8k CHR high nibble
		if (this->prg_mode == 2) {
			this->prg_seg_conf = 0x5555;
			this->prg_bank[0] = this->prg_bank[0]*2 + 0;
			this->prg_bank[4] = 0xffff;
		}
	}

	u16 RST_vec = ((u16)(this->prg[this->prg_size-4] & 0xff) | (u16)(this->prg[this->prg_size-3] & 0xff) << 8);
	if (RST_vec < 0xc000) this->prg_mode |= 0x3000;
	if (prg_size < 32768) this->prg_mode |= 0x3000;

	this->cycles = 0;
	for (int i = 0; i < 0x6000; ++i) this->mem[i] = i ^ (i>>8);
	for (int i = 0; i < 0x8000; ++i) this->mode20_ram[i] = i ^ (i>>8) ^ 0x80;
	for (int i = 0; i < (int)chr_size && i < 8192; ++i) this->mem[i + 0x1000] = this->chr[i];
	// TODO load save RAM to 0x6000-0x7fff

	if (((this->prg_mode & 0x001f) == 0) && prg_size <= 32768 && chr_size <= 8192) {  // is a RAM cart
		// copy to mode20_ram so that rom writes will be saved by the retrogress button
		for (size_t i = 0; i < prg_size; ++i) this->mode20_ram[i] = this->prg[i];
		this->prg = this->mode20_ram;
	}

	for (int c = 0; c < 8; ++c) this->audio_chn_info[c] = 0;
	for (int c = 0; c < 4; ++c) this->audio_chn_length[c] = 0;
	for (int c = 0; c < 7; ++c) this->audio_chn_phase[c] = 0;
	this->render_option = 0x00;
	this->vid_mode = 0;
	this->vid_tile_ptr = 0;
	this->vid_sprite_page = 0x0e;
	this->vid_sprite_ptr = 0;
	this->vid_scroll_x = 0;
	this->vid_scroll_y = 0;
	this->p1 = 0;
	this->p2 = 0;
	this->p1_in = 0;
	this->p2_in = 0;
	this->vsync_state = 0;
	this->audio_chn_enable = 0;
	this->raw_pcm_len = 0;
	this->cpu.irq = this->cpu.flags = 1;
	this->snd_gen_rand = 0x8000000000000000ULL;
	for (int i = 0; i < 4; i++) this->mode4_reg[i] = 0;
	for (int i = 0; i < 4; i++) this->mode9_reg[i] = 0;
	for (int i = 0; i < 4; i++) this->mode12_reg[i] = 0;
	this->mode9_shift = 0;
	this->mode4_reg[3] = 0xff;
	this->mode9_reg[0] = 0x0c;
	this->chr_bank[6*2+1] = 0xfe;
	this->chr_bank[7*2+1] = 0xff;
	this->mode12_irq = false;

	retro_imitator_state_save(this);
}

// Convert a randomized 64 bit int to a double in the range 0.0 <= x < 1.0
// from https://github.com/mattiasgustavsson/libs/blob/main/rnd.h
static double retro_imitator_rand_float(u64* state)
{
	// RNG is xorshift64
	u64 x = *state;
	x ^= x << 13;
	x ^= x >> 7;
	x ^= x << 17;
	*state = x;

	u64 exponent = 0x3ff;
	u64 mantissa = x >> 12;
	u64 result = ( exponent << 52 ) | mantissa;
	double fresult;
	memcpy(&fresult, &result, sizeof(double));
	return fresult - 1.0;
}

#define RETRO_IMITATOR_AUDIO_FREQ 32000

void retro_imitator_audio_render(retro_imitator_state* this, float* stream, int stream_len)
{
	if (!this || this->init_magic != RETRO_IMITATOR_INIT_MAGIC) return;
	for (int i = 0; i < stream_len; i++) stream[i] = 0;
	if (!this) return;
	// channel 0~6: pulse 1, pulse 2, organ, buzz, extra, extra, extra
	for (int c = 0; c < 7; ++c) {
		int info_vol = (this->audio_chn_info[c] & 0x0f);
		int info_t = (this->audio_chn_info[c] & 0x07ff0000) >> 16;
		int info_duty = (this->audio_chn_info[c] & 0xc0) >> 6;
		if (c == 2) {
			if (info_vol) info_vol = 0xf;
			info_t = (info_t << 1) + 1;
		}
		if (c == 3) {
			if (!(info_t & 0x80) && !(this->render_option & (1<<0))) info_vol = 0;
			const int buzz_tbl[16] = {22, 45, 91, 184, 368, 552, 737, 929, 1171, 1475, 2211, 2952, 4423, 5904, 11810, 23621};
			info_t = buzz_tbl[info_t & 0x0f];
			info_duty = 4;
		}
		if ((c < 3) && this->audio_chn_length[c] == 0) info_vol = 0;
		if ((c < 3) && (this->audio_chn_length[c] > 1)) {
			if ((c == 2) && !(this->audio_chn_info[c] & 0x80)) this->audio_chn_length[c] -= 2;
			if ((c != 2) && !(this->audio_chn_info[c] & 0x20)) this->audio_chn_length[c] -= 2;
		}

		const double duty_table[5] = {8.0, 4.0, 2.0, 3.0, 24.0};
		double duty = duty_table[info_duty];
		double vol = info_vol * ((c == 2) ? 0.005 : 0.01);
		bool enabled = (this->audio_chn_enable & (1 << c));

		if ((4 <= c) && (c < 7)) {
			enabled = !!(this->audio_chn_info[c] & 0x80000000);
			info_t = (this->audio_chn_info[c] & 0x0ffe0000) >> 17;
			info_duty = (this->audio_chn_info[c] & 0xf0) >> 4;
			duty = 16.0 / (double)(info_duty+1);
			if (c == 6) {
				info_vol = (this->audio_chn_info[c] & 0x3f);
				duty = 1.0;
			}
			vol = info_vol * 0.005;
		}

		double freq = 111760.0 / (info_t+1);
		// or with a silly switch, invert the notes around middle C
		if (this->render_option & (1<<2)) freq = 0.612454692366301 * (info_t+1);

		if ((info_vol == 0) || (!enabled) || ((RETRO_IMITATOR_AUDIO_FREQ/2) < freq)) {
			this->audio_chn_phase[c] = 0;
			continue;
		}
		double phase = this->audio_chn_phase[c];

		this->audio_chn_phase[c] += freq * stream_len / (double)RETRO_IMITATOR_AUDIO_FREQ;
		const double wave_wrap[7] = {24.0, 24.0, 1.0, 24.0, 720720.0, 720720.0, 720720.0};
		while (this->audio_chn_phase[c] >= wave_wrap[c]) this->audio_chn_phase[c] -= wave_wrap[c];
		if (this->audio_chn_mute & (1<<c)) continue;
		for (int i = 0; i < stream_len; i++) {
			const double tau = 6.28318530717958647693;
			double pcm = 0;
			if (c == 2) {
				for (int n = 1; n <= 7; ++n) {
					double h = 1<<(n-1);
					if ((RETRO_IMITATOR_AUDIO_FREQ/2) < (freq * h)) break;
					pcm += sin(tau * (phase) * h) / h;
					if (this->render_option & (1<<0)) break;
				}
			} else {
				if (this->render_option & (1<<0)) {
					pcm += sin(tau * phase);
				} else {
					pcm += sin(tau * phase + sin(tau * phase * duty));
				}
			}
			if ((this->render_option & (1<<0)) && (freq * 32 < (RETRO_IMITATOR_AUDIO_FREQ/2))) pcm += sin(tau * phase * 32) / 32;  // extra harmonic for sine only mode
			phase += freq / (double)RETRO_IMITATOR_AUDIO_FREQ;
			stream[i] += pcm * vol;
		}
	}
	// also channel 3: noise
	int noise_vol = (this->audio_chn_info[3] & 0x0f);
	int noise_t = (this->audio_chn_info[3] & 0x008f0000) >> 16;
	if (noise_t & 0x80) noise_vol = 0;
	if (this->audio_chn_length[3] == 0) noise_vol = 0;
	if (!(this->audio_chn_info[3] & 0x20) && (this->audio_chn_length[3] > 1)) this->audio_chn_length[3] -= 2;
	if (noise_vol && !(this->audio_chn_mute & (1<<3)) && !(this->render_option & (1<<0))) {
		const double filter_coefficients[16*2] = {-1, 1, -1, 1, -1, 1, -1, 1, -1, 1, -1, 1,
			-0.8092397977814331, 1.1907602022185668,
			-0.487416723609962, 1.512583276390038,
			-0.15770920986764572, 1.8422907901323544,
			0.20879235040960928, 2.208792350409609,
			1.0118563368238904, 3.0118563368238904,
			1.7776068539149752, 3.777606853914975,
			3.258535201285486, 5.258535201285485,
			4.729741646724314, 6.729741646724314,
			10.54609267964924, 12.546092679649236,
			22.13540914642368, 24.135409146423676};
		double coefficient = filter_coefficients[(noise_t&0xf) * 2 + 0];
		double scale = filter_coefficients[(noise_t&0xf) * 2 + 1];
		double x, x_prev, pcm_prev;
		pcm_prev = (retro_imitator_rand_float(&this->snd_gen_rand)-0.5)*2;
		x_prev = pcm_prev;
		for (int i = -48; i < stream_len; i++) {
			x = (retro_imitator_rand_float(&this->snd_gen_rand)-0.5)*2;
			double pcm = (x + x_prev + pcm_prev * coefficient) / scale;
			pcm_prev = pcm;
			x_prev = x;
			if (i < 0) continue; // prime the filter with some samples, instead of storing state
			stream[i] += pcm * noise_vol * 0.005;
		}
	}
	// channel 4: pwm
	if (this->pwm_triggered) {
		this->pwm_len = ((this->audio_chn_info[7] & 0xff000000) >> 20) + 1;
		this->pwm_addr = ((this->audio_chn_info[7] & 0x00ff0000) >> 10) | 0x4000;
		this->pwm_rate = this->audio_chn_info[7] & 0x0000000f;
		this->pwm_playhead = 0;
		if (this->pwm_len <= 1) this->pwm_len = 0;
		this->x1 = 0.0;
		this->x2 = 0.0;
		this->x3 = 0.0;
		this->y1 = 0.0;
		this->y2 = 0.0;
		this->y3 = 0.0;
		this->pwm_triggered = false;
	}
	const int pwm_tbl[16] = {92, 82, 73, 69, 61, 55, 49, 46, 41, 34, 31, 27, 23, 18, 15, 11};
	int cur_pos = this->pwm_playhead;
	int end_pos = (this->pwm_len * pwm_tbl[this->pwm_rate] * 8);
	if (cur_pos < end_pos) {
		u8 pwm[4096];
		for (int i = 0, pwm_addr = this->pwm_addr; i < this->pwm_len; ++i, ++pwm_addr) {
			pwm[i] = retro_imitator_bus_io(this, pwm_addr | 0x8000, 0, 0xa);
		}
		const double filter_coefficients[16*4] = {
			16193637.073124193, -16070866.519608255, 5316495.181835737, 5439273.735351675,
			11455784.27587703, -11358343.010900686, 3754046.1359012853, 3851495.4008776303,
			8104537.321549915, -8027199.926209289, 2650320.9860304007, 2727666.381371028,
			6816937.836076086, -6748038.995244093, 2226726.0478918427, 2295632.8887238353,
			4823164.571260149, -4768481.312973633, 1571573.82802373, 1626265.086310245,
			3412751.6450653328, -3369351.302120504, 1108924.108911303, 1152332.451856132,
			2414962.3098919517, -2380517.222786068, 782267.5252806256, 816720.6123865093,
			2031546.0607077251, -2000859.9222961243, 656954.1835868619, 687648.3219984627,
			1437767.222076268, -1413413.4057911306, 463223.9649227716, 487585.78120790893,
			856174.0820475892, -838955.8717815361, 274083.5418902314, 291309.75215628464,
			606090.8019982174, -592426.4873534509, 193072.61773061036, 206744.93237537678,
			429107.92699071666, -418264.3411623556, 135941.8002382296, 146793.38606659058,
			255693.6733263347, -248028.63859310863, 80233.98054455155, 87907.01527777765,
			128279.01447245546, -123453.55204384726, 39631.37483436061, 44464.837262968795,
			76502.54379586168, -73092.96484206546, 23301.71923568666, 26719.298189482888,
			32355.46220174386, -30445.702996995395, 9566.340446237093, 11484.099650985558
		};
		int rate = pwm_tbl[this->pwm_rate];
		double cof1 = filter_coefficients[this->pwm_rate * 4 + 0];
		double cof2 = filter_coefficients[this->pwm_rate * 4 + 1];
		double cof3 = filter_coefficients[this->pwm_rate * 4 + 2];
		double scale = filter_coefficients[this->pwm_rate * 4 + 3];
		int bit_index = cur_pos/rate;
		int bit = (pwm[bit_index/8] & (1<<(bit_index%8))) ? 1 : -1;
		if (this->render_option & (1<<1)) bit = (pwm[bit_index/8] & (1<<(7-(bit_index%8)))) ? 1 : -1;
		if (!(this->audio_chn_mute & (1<<7))) {
			double x1 = this->x1;
			double x2 = this->x2;
			double x3 = this->x3;
			double y1 = this->y1;
			double y2 = this->y2;
			double y3 = this->y3;
			for (int i = 0; i < stream_len*12; i++) {
				if (cur_pos >= end_pos) {
					bit = 0;
				} else if ((cur_pos % rate) == 0) {
					bit_index = cur_pos/rate;
					bit = (pwm[bit_index/8] & (1<<(bit_index%8))) ? 1 : -1;
					if (this->render_option & (1<<1)) bit = (pwm[bit_index/8] & (1<<(7-(bit_index%8)))) ? 1 : -1;
				}
				double pcm = (bit + x1*3 + x2*3 + x3 + y1*cof1 + y2*cof2 + y3*cof3) / scale;
				if ((i % 12) == 0) {
					stream[(i/12)] += pcm * 0.08;
				}
				x3 = x2;
				x2 = x1;
				x1 = bit;
				y3 = y2;
				y2 = y1;
				y1 = pcm;
				cur_pos++;
			}
			this->x1 = x1;
			this->x2 = x2;
			this->x3 = x3;
			this->y1 = y1;
			this->y2 = y2;
			this->y3 = y3;
		}
		this->pwm_playhead += stream_len*12;
	}

	// also channel 4: pcm
	if (this->raw_pcm_len >= 4 && !(this->audio_chn_mute & (1<<7))) {
		for (int i = 0; i < stream_len; i++) {
			// Linear interpolate all the samples this frame (ignoring cpu cycles between)
			double t = (i + 1.0) * (double)(this->raw_pcm_len - 1) / (double)stream_len;
			int k = t;
			t -= k;
			double x = ((((this->raw_pcm[k] & 0x7f) * (1.0 - t)) + ((this->raw_pcm[k+1] & 0x7f) * (t))) - 63.5);
			stream[i] += x * 0.005;
		}
	}
	if (this->raw_pcm_len >= 4) { // for next frame interpolation
		this->raw_pcm[0] = this->raw_pcm[this->raw_pcm_len-1];
		this->raw_pcm_len = 1;
	} else {
		this->raw_pcm_len = 0;
	}
	// 3 hz high pass to remove DC offset (for example with bytebeat)
	for (int i = 0; i < stream_len; i++) {
		double pcm = ((stream[i] - this->high_pass_filter_x) + this->high_pass_filter_y * 0.9998) / 1.0002;
		this->high_pass_filter_x = stream[i];
		this->high_pass_filter_y = pcm;
		stream[i] = pcm;
	}
}

#ifdef __LIBRETRO__
#include "libretro.h"

unsigned retro_api_version(void) { return RETRO_API_VERSION; }
void retro_get_system_info(struct retro_system_info *info)
{
	for(size_t i = 0; i < sizeof(*info); ++i) ((uint8_t*)info)[i] = 0;
	info->library_name     = "Retro Imitator";
	info->library_version  = "1.6.8-libretro";
	info->valid_extensions = NULL; // this core accepts anything
	info->need_fullpath    = false;
	info->block_extract    = false;
}
static retro_environment_t retro_environment;
void retro_set_environment(retro_environment_t cb) {
	retro_environment = cb;
	struct retro_variable options[] = {
		{ "retro_imitator_mute_1", "Mute pulse 1; false|true" },
		{ "retro_imitator_mute_2", "Mute pulse 2; false|true" },
		{ "retro_imitator_mute_3", "Mute organ; false|true" },
		{ "retro_imitator_mute_4", "Mute noise; false|true" },
		{ "retro_imitator_mute_5", "Mute ex pulse 1; false|true" },
		{ "retro_imitator_mute_6", "Mute ex pulse 2; false|true" },
		{ "retro_imitator_mute_7", "Mute ex saw; false|true" },
		{ "retro_imitator_mute_8", "Mute PWM and PCM; false|true" },
		{ "retro_imitator_sinewave", "Sinewave only; false|true" },
		{ "retro_imitator_pwm_rev", "PWM bit reverse; false|true" },
		{ "retro_imitator_inv_freq", "Invert freq; false|true" },
		{ "retro_imitator_no_shade", "Remove shading; false|true" },
		{ "retro_imitator_sprite_warp", "Sprite warp; false|true" },
		{ "retro_imitator_mode12_irq", "Mode 12 IRQ fix; false|true" },
		{ NULL, NULL },
	};
	retro_environment(RETRO_ENVIRONMENT_SET_VARIABLES, (void*)options);
}
static retro_video_refresh_t retro_video_refresh;
static retro_audio_sample_t retro_audio_sample;
static retro_audio_sample_batch_t retro_audio_sample_batch;
static retro_input_poll_t retro_input_poll;
static retro_input_state_t retro_input_state;
void retro_set_video_refresh(retro_video_refresh_t cb) { retro_video_refresh = cb; }
void retro_set_audio_sample(retro_audio_sample_t cb) { retro_audio_sample = cb; }
void retro_set_audio_sample_batch(retro_audio_sample_batch_t cb) { retro_audio_sample_batch = cb; }
void retro_set_input_poll(retro_input_poll_t cb) { retro_input_poll = cb; }
void retro_set_input_state(retro_input_state_t cb) { retro_input_state = cb; }

#define AUDIO_BUF_SIZE (RETRO_IMITATOR_AUDIO_FREQ*16/1000)

typedef struct libretro_context_t {
	retro_imitator_state state;
	void* frame_buffer;
	void* audio_buffer;
	void* game_data;
	size_t game_data_size;
	u32 palette_32[256];
	u16 palette_16[256];
	u32 prev_keys;
	bool pixelfmt_is_32bit;
	bool reset_request;
} libretro_context_t;

static libretro_context_t *retro_ctx;

void retro_init(void) {
	retro_ctx = malloc(sizeof(libretro_context_t));
	if (!retro_ctx) return;
	for(size_t i = 0; i < sizeof(libretro_context_t); ++i) ((u8*)retro_ctx)[i] = 0;
	retro_ctx->audio_buffer = malloc(AUDIO_BUF_SIZE * 2 * sizeof(s16));
	retro_ctx->frame_buffer = malloc(320 * 270 * sizeof(u32));
	if (!retro_ctx->audio_buffer || !retro_ctx->frame_buffer) {
		free(retro_ctx);
		retro_ctx = NULL;
		return;
	}
	for(size_t i = 0; i < AUDIO_BUF_SIZE * 2 * sizeof(s16); ++i) ((u8*)retro_ctx->audio_buffer)[i] = 0;
	for(size_t i = 0; i < 320 * 270 * sizeof(u32); ++i) ((u8*)retro_ctx->frame_buffer)[i] = 0;
	retro_ctx->reset_request = false;

	for (int c = 0; c < 192; ++c) {
		u8 mul = ((c&0xc0) ? 64 : 85);
		u8 add = ((c&0x80) ? 63 : 0);
		u8 r = ((c & 12) >> 2) * mul + add;
		u8 g = ((c & 48) >> 4) * mul + add;
		u8 b = ((c & 3) >> 0) * mul + add;
		retro_ctx->palette_32[c] = (r<<16) | (g<<8) | (b<<0);
		r = r * 31 / 255;
		g = g * 31 / 255;
		b = b * 31 / 255;
		retro_ctx->palette_16[c] = (r<<10) | (g<<5) | (b<<0);
	}
	for (int c = 192; c < 256; ++c) {
		u8 r = (((c & 12) >> 2) * 85) / 2;
		u8 g = (((c & 48) >> 4) * 85) / 2;
		u8 b = (((c & 3) >> 0) * 85) / 2;
		retro_ctx->palette_32[c] = (r<<16) | (g<<8) | (b<<0);
		r = r * 31 / 255;
		g = g * 31 / 255;
		b = b * 31 / 255;
		retro_ctx->palette_16[c] = (r<<10) | (g<<5) | (b<<0);
	}
}

bool retro_load_game(const struct retro_game_info *info)
{
	if (!retro_ctx) return false;
	size_t s = info->size;
	retro_ctx->game_data = malloc(s);
	if (!retro_ctx->game_data) return false;
	retro_ctx->game_data_size = s;
	memcpy(retro_ctx->game_data, info->data, s);

	retro_imitator_init(&retro_ctx->state, retro_ctx->game_data, retro_ctx->game_data_size);

	return true;
}

void retro_get_system_av_info(struct retro_system_av_info *info)
{
	info->geometry = (struct retro_game_geometry) {
		.base_width   = 316,
		.base_height  = 270,
		.max_width    = 320,
		.max_height   = 270,
		.aspect_ratio = 4.0/3.0,
	};
	info->timing = (struct retro_system_timing) {
		.fps = 62.5f,
		.sample_rate = RETRO_IMITATOR_AUDIO_FREQ,
	};
	if (!retro_ctx) return;
	enum retro_pixel_format rgbfmt = RETRO_PIXEL_FORMAT_XRGB8888;
	retro_ctx->pixelfmt_is_32bit = retro_environment(RETRO_ENVIRONMENT_SET_PIXEL_FORMAT, &rgbfmt);
}

void retro_reset(void) {
	if (!retro_ctx) return;
	retro_ctx->reset_request = true;
}

void retro_run(void)
{
	if (!retro_ctx) return;
	libretro_context_t *this = retro_ctx;

	u16 pads_p1 = 0;
	u16 pads_p2 = 0;
	retro_input_poll();
	for (int i = 0; i < 16; ++i) {
		pads_p1 |= (retro_input_state(0, RETRO_DEVICE_JOYPAD, 0, i)) << i;
		pads_p2 |= (retro_input_state(1, RETRO_DEVICE_JOYPAD, 0, i)) << i;
	}

	bool options_changed = false;
	if (retro_environment(RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE, &options_changed) && (options_changed || !this->state.cycles)) {
		u16 new_options = 0;
		char *option_variable_str[14] = {
			"retro_imitator_mute_1", "retro_imitator_mute_2", "retro_imitator_mute_3", "retro_imitator_mute_4", "retro_imitator_mute_5", "retro_imitator_mute_6", "retro_imitator_mute_7", "retro_imitator_mute_8", "retro_imitator_sinewave", "retro_imitator_pwm_rev", "retro_imitator_inv_freq", "retro_imitator_no_shade", "retro_imitator_sprite_warp", "retro_imitator_mode12_irq"
		};
		for (int i = 0; i < 14; ++i) {
			struct retro_variable var = {0};
			var.key = option_variable_str[i];
			retro_environment(RETRO_ENVIRONMENT_GET_VARIABLE, &var);
			if (var.value && (var.value[0] == 't')) {
				new_options |= 1<<i;
			}
		}
		retro_ctx->state.audio_chn_mute = new_options & 0x00ff;
		retro_ctx->state.render_option = (new_options & 0x3f00) >> 8;
	}

	retro_imitator_advance_frame(&retro_ctx->state, pads_p1&0x0fff, pads_p2&0x0fff, retro_ctx->reset_request, retro_ctx->state.render_option, retro_ctx->state.audio_chn_mute);
	retro_ctx->reset_request = false;

	float audio_buffer[AUDIO_BUF_SIZE];
	retro_imitator_audio_render(&this->state, audio_buffer, AUDIO_BUF_SIZE);

	s16 *abuf = (s16 *)this->audio_buffer;
	for (int i = 0; i < AUDIO_BUF_SIZE; ++i) {
		float x = audio_buffer[i];
		x = (-1.0 <= x) ? x : -1.0;
		x = (1.0 <= x) ? 1.0 : x;
		abuf[i*2+0] = x * 32767.0;
		abuf[i*2+1] = x * 32767.0;
	}

	// make internals read only visible at 0x0e00 every nmi and for sprites rendering.
	retro_imitator_state_save(&this->state);

	u8 screen_pixels[320 * 270];
	retro_imitator_render(&this->state, screen_pixels, 320*sizeof(u8), 316, 270, 30, 15);
	u8 bg_c = retro_imitator_get_bg_color(&this->state);
	for (int c = 192; c < 256; ++c) {
		u8 r = ((((c & 12) >> 2) * 85) + (((bg_c & 12) >> 2) * 85)) / 2;
		u8 g = ((((c & 48) >> 4) * 85) + (((bg_c & 48) >> 4) * 85)) / 2;
		u8 b = ((((c &  3) >> 0) * 85) + (((bg_c &  3) >> 0) * 85)) / 2;
		retro_ctx->palette_32[c] = (r<<16) | (g<<8) | (b<<0);
		r = r * 31 / 255;
		g = g * 31 / 255;
		b = b * 31 / 255;
		retro_ctx->palette_16[c] = (r<<10) | (g<<5) | (b<<0);
	}

	retro_audio_sample_batch(abuf, AUDIO_BUF_SIZE);

	if (this->pixelfmt_is_32bit) {
		u32 *vbuf = (u32 *)this->frame_buffer;
		for (int i = 0; i < 320 * 270; ++i) {
			vbuf[i] = this->palette_32[screen_pixels[i]];
		}
		retro_video_refresh(vbuf, 316, 270, 320 * sizeof(u32));
	} else {
		u16 *vbuf = (u16 *)this->frame_buffer;
		for (int i = 0; i < 320 * 270; ++i) {
			vbuf[i] = this->palette_16[screen_pixels[i]];
		}
		retro_video_refresh(vbuf, 316, 270, 320 * sizeof(u16));
	}
}

void retro_unload_game(void) {
	if (!retro_ctx) return;
	retro_ctx->state.init_magic = 0;
	free(retro_ctx->game_data);
	retro_ctx->game_data = NULL;
	retro_ctx->game_data_size = 0;
}

void retro_deinit(void) {
	if (retro_ctx) {
		free(retro_ctx->frame_buffer);
		free(retro_ctx->audio_buffer);
		free(retro_ctx->game_data);
	}
	free(retro_ctx);
	retro_ctx = NULL;
}

size_t retro_serialize_size(void)
{
	return 0x6000+0x8000;
}

bool retro_serialize(void *data_, size_t size)
{
	assert(sizeof(double) == sizeof(u64));
	if (!retro_ctx) return false;
	if (size < retro_serialize_size()) return false;
	u8* p = (u8*)data_;
	retro_imitator_state* state = &retro_ctx->state;
	if (state->init_magic != RETRO_IMITATOR_INIT_MAGIC) return false;

	retro_imitator_state_save(state);
	for (int i = 0; i < 0x6000; ++i) *p++ = state->mem[i];
	for (int i = 0; i < 0x8000; ++i) *p++ = state->mode20_ram[i];

	assert((size_t)(p - (u8*)data_) == retro_serialize_size());
	return true;
}

bool retro_unserialize(const void *data_, size_t size)
{
	assert(sizeof(double) == sizeof(u64));
	if (!retro_ctx) return false;
	if (size < retro_serialize_size()) return false;
	u8* p = (u8*)data_;
	retro_imitator_state* state = &retro_ctx->state;

	u32 init_magic = READ32LE(p+0xe00);
	if (init_magic != RETRO_IMITATOR_INIT_MAGIC) return false;

	for (int i = 0; i < 0x6000; ++i) state->mem[i] = *p++;
	for (int i = 0; i < 0x8000; ++i) state->mode20_ram[i] = *p++;
	retro_imitator_state_restore(state);

	assert((size_t)(p - (u8*)data_) == retro_serialize_size());
	return true;
}

// Unused libretro API
bool retro_load_game_special(unsigned type, const struct retro_game_info *info, size_t num) { (void)type; (void)info; (void)num; return false; }
void retro_set_controller_port_device(unsigned port, unsigned device) { (void)port; (void)device; }
unsigned retro_get_region(void) { return RETRO_REGION_NTSC; }
void *retro_get_memory_data(unsigned id) { (void)id; return NULL; }
size_t retro_get_memory_size(unsigned id) { (void)id; return 0; }
void retro_cheat_reset(void) { }
void retro_cheat_set(unsigned index, bool enabled, const char *code) { (void)index; (void)enabled; (void)code; }
#endif


#ifndef __LIBRETRO__
#include <stdio.h>
#include <time.h>
#include <assert.h>
#include <math.h>
#include "SDL.h"
#ifdef __EMSCRIPTEN__
#include <emscripten.h>
#endif

#define AUDIO_BUF_SIZE (RETRO_IMITATOR_AUDIO_FREQ*16/1000)
#define AUDIO_SDL_BUF_SIZE 512
#define RETROGRESS_HEAP_SIZE 16777216

const u8 gDefaultRomData[512] = {
  0x52, 0x65, 0x74, 0x72, 0x6f, 0x20, 0x49, 0x6d, 0x69, 0x74, 0x61, 0x74,
  0x6f, 0x72, 0x0a, 0x43, 0x6f, 0x70, 0x79, 0x72, 0x69, 0x67, 0x68, 0x74,
  0x20, 0x28, 0x43, 0x29, 0x20, 0x32, 0x30, 0x32, 0x34, 0x20, 0x4a, 0x6f,
  0x68, 0x6e, 0x61, 0x74, 0x68, 0x61, 0x6e, 0x20, 0x52, 0x6f, 0x61, 0x74,
  0x63, 0x68, 0x2c, 0x20, 0x61, 0x6c, 0x6c, 0x20, 0x72, 0x69, 0x67, 0x68,
  0x74, 0x73, 0x20, 0x72, 0x65, 0x73, 0x65, 0x72, 0x76, 0x65, 0x64, 0x2e,
  0x0a, 0x0a, 0x44, 0x72, 0x6f, 0x70, 0x20, 0x61, 0x20, 0x67, 0x61, 0x6d,
  0x65, 0x20, 0x66, 0x69, 0x6c, 0x65, 0x20, 0x6f, 0x6e, 0x20, 0x74, 0x68,
  0x69, 0x73, 0x20, 0x77, 0x69, 0x6e, 0x64, 0x6f, 0x77, 0x20, 0x74, 0x6f,
  0x20, 0x62, 0x65, 0x67, 0x69, 0x6e, 0x0a, 0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00, 0x00, 0x00, 0x4c, 0xad, 0xde, 0xa9, 0x07, 0xa0, 0xe0,
  0x99, 0x00, 0x0f, 0xc8, 0xd0, 0xfa, 0x8c, 0x06, 0x20, 0x8c, 0x06, 0x20,
  0x88, 0xa2, 0x08, 0x9c, 0x07, 0x20, 0xca, 0xd0, 0xfa, 0xc8, 0xb9, 0x37,
  0xff, 0xf0, 0xf2, 0x38, 0x2a, 0x85, 0x00, 0xa9, 0x00, 0x90, 0x06, 0xc8,
  0x30, 0x0c, 0xb9, 0x37, 0xff, 0x8d, 0x07, 0x20, 0x06, 0x00, 0xd0, 0xf1,
  0xf0, 0xdb, 0xa9, 0x1f, 0x8d, 0x06, 0x20, 0xa0, 0x00, 0x8c, 0x06, 0x20,
  0xa2, 0x05, 0x9c, 0x07, 0x20, 0xc8, 0xd0, 0xfa, 0xca, 0xd0, 0xf7, 0xa9,
  0x2f, 0x8d, 0x14, 0x40, 0x88, 0xd0, 0x05, 0x29, 0x7f, 0x8d, 0x00, 0x20,
  0xc8, 0xb9, 0xa2, 0xff, 0xf0, 0x9f, 0x30, 0xf3, 0x8d, 0x06, 0x20, 0xc8,
  0xb9, 0xa2, 0xff, 0x8d, 0x06, 0x20, 0xc8, 0xbe, 0xa2, 0xff, 0xc8, 0xb9,
  0xa2, 0xff, 0xf0, 0x08, 0x8d, 0x07, 0x20, 0xca, 0xd0, 0xf4, 0xf0, 0xdc,
  0xb9, 0xa1, 0xff, 0x8d, 0x07, 0x20, 0xca, 0xd0, 0xfa, 0xf0, 0xd1, 0x00,
  0x75, 0x66, 0x40, 0x00, 0x40, 0x00, 0x60, 0x66, 0x00, 0x75, 0x66, 0x02,
  0x00, 0x02, 0x00, 0x55, 0x40, 0x00, 0x40, 0x00, 0x55, 0x02, 0x00, 0x02,
  0x00, 0x57, 0x40, 0x00, 0x40, 0x66, 0x00, 0x03, 0x66, 0x00, 0x57, 0x02,
  0x00, 0x02, 0x66, 0x00, 0x00, 0xc0, 0x01, 0x03, 0xc0, 0x80, 0xc0, 0x80,
  0x03, 0x80, 0xc0, 0xff, 0x18, 0x3c, 0x3e, 0x1f, 0x0f, 0x07, 0x03, 0x01,
  0x8f, 0x03, 0x83, 0xc3, 0xe3, 0xf3, 0x8f, 0xc0, 0xc1, 0xc3, 0xc7, 0xcf,
  0xff, 0x18, 0x3c, 0x7c, 0xf8, 0xf0, 0xe0, 0xc0, 0x80, 0xff, 0xfb, 0x7f,
  0x3f, 0x1f, 0x0f, 0x07, 0x03, 0x01, 0xff, 0xdf, 0xfe, 0xfc, 0xf8, 0xf0,
  0xe0, 0xc0, 0x80, 0x80, 0xf0, 0x80, 0x0f, 0x88, 0xff, 0x00, 0xc4, 0x20,
  0x41, 0x1a, 0x04, 0x00, 0x20, 0x3e, 0x1b, 0x03, 0x05, 0x00, 0x21, 0x6f,
  0x03, 0x09, 0x0b, 0x0b, 0x21, 0x70, 0x03, 0x0a, 0x0c, 0x0c, 0x22, 0x0c,
  0x02, 0x13, 0x13, 0x22, 0x13, 0x02, 0x14, 0x14, 0xc0, 0x20, 0x21, 0x1d,
  0x01, 0x02, 0x02, 0x00, 0x23, 0x81, 0x1d, 0x06, 0x07, 0x00, 0x21, 0xce,
  0x04, 0x0d, 0x0e, 0x0f, 0x10, 0x21, 0xef, 0x02, 0x11, 0x12, 0x22, 0x4c,
  0x08, 0x15, 0x00, 0x23, 0x9e, 0x01, 0x08, 0x33, 0xe0, 0x04, 0x01, 0x3b,
  0x3f, 0x0f, 0x00, 0x00, 0x78, 0xa2, 0xff, 0x9a, 0x8e, 0xf2, 0xff, 0x6c,
  0xfc, 0xff, 0xad, 0xfe, 0xb0, 0xfe, 0xad, 0xfe};
const int gDefaultRomSize = 512;

// Reads files without fseek and ftell capabilities (stdin, named pipes)
// this does so by using realloc a bunch of times.
// returns 0 and sets data_ptr to NULL if error or zero sized file.
size_t read_whole_file_without_fseek(u8** data_ptr, FILE* f)
{
	u8* data = NULL;
	size_t length = 0;
	u8* realloc_data = NULL;
	// I want a slower exponential growth then something like a simple
	// "capacity *= 2", so I choose the fibonacci sequence (growth rate of about 1.6).
	// 4KiB (common memory page size) times the 6th fibonacci number is 32kiB.
	size_t capacity = 32768;
	size_t previous_capacity = 20480;

	assert(data_ptr && f);
	*data_ptr = NULL;

	data = malloc(capacity);
	if (!data) return 0;

	while (!feof(f)) {
		length += fread(data + length, sizeof(u8), capacity - length, f);
		if (ferror(f)) goto free_and_return_empty;

		if (length == capacity) {
			size_t n = capacity;
			capacity = capacity + previous_capacity;
			previous_capacity = n;
			realloc_data = realloc(data, capacity);
			if (!realloc_data) goto free_and_return_empty;
			data = realloc_data;
		}
	}
	if (!length) goto free_and_return_empty;
	// shrink buffer to fit the final size
	realloc_data = realloc(data, length);
	if (!realloc_data) goto free_and_return_empty;
	data = realloc_data;

	*data_ptr = data;
	return length;
free_and_return_empty:
	free(data);
	return 0;
}

// returns 0 and sets data_ptr to NULL if error or zero sized file.
size_t read_whole_file(u8** data_ptr, FILE* f)
{
	assert(data_ptr && f);
	*data_ptr = NULL;

	if (fseek(f, 0, SEEK_END) != 0) {
		// If fseek fails the file cursor is still at the beginning
		// so no need for rewind(f)
		return read_whole_file_without_fseek(data_ptr, f);
	}

	// fseek works, so now do it the simple way
	size_t length = ftell(f);
	if (length <= 0) return 0;
	u8* data = malloc(length);
	if (!data) return 0;
	rewind(f);
	size_t read_length = fread(data, sizeof(u8), length, f);
	if (read_length != length) {
		free(data);
		return 0;
	}

	*data_ptr = data;
	return length;
}

typedef struct sdl_context_t {
	retro_imitator_state state;
	s16 audio_buf[AUDIO_BUF_SIZE*3 + AUDIO_SDL_BUF_SIZE];
	SDL_Surface* screen;
	u8 *rom;
	size_t rom_size;
	int error;
	u8 keypad_p1;
	u8 keypad_p2;
	bool pause;
	bool reset_request;
	bool next_frame;
	bool do_retrogress;
	u8 *retrogress_heap;
	u32 retrogress_head;
	u32 retrogress_tail;
#ifndef __EMSCRIPTEN__
	bool take_screenshot;
	bool recording_video;
	bool send_video_to_stdout;
	FILE *video_out_file;
#endif
} sdl_context_t;

static sdl_context_t main_ctx;


#ifndef __EMSCRIPTEN__
const u8 video_stream_header[159] = {
  0x6e, 0x75, 0x74, 0x2f, 0x6d, 0x75, 0x6c, 0x74, 0x69, 0x6d, 0x65, 0x64,
  0x69, 0x61, 0x20, 0x63, 0x6f, 0x6e, 0x74, 0x61, 0x69, 0x6e, 0x65, 0x72,
  0x00, 0x4e, 0x4d, 0x7a, 0x56, 0x1f, 0x5f, 0x04, 0xad, 0x2c, 0x04, 0x01,
  0x02, 0x7f, 0x02, 0x01, 0x81, 0xfa, 0x00, 0x02, 0x7d, 0xc0, 0x00, 0x02,
  0x00, 0x53, 0xa0, 0x00, 0x02, 0x01, 0x01, 0x21, 0x03, 0x87, 0x7f, 0x01,
  0x00, 0x21, 0x03, 0x01, 0x01, 0x01, 0xc0, 0x00, 0x02, 0x00, 0x81, 0x29,
  0x00, 0x02, 0xd3, 0x44, 0x4b, 0x8e, 0x4e, 0x53, 0x11, 0x40, 0x5b, 0xf2,
  0xf9, 0xdb, 0x18, 0x00, 0x01, 0x04, 0x50, 0x53, 0x44, 0x10, 0x00, 0x00,
  0x81, 0xfa, 0x00, 0x00, 0x01, 0x00, 0x81, 0xfa, 0x00, 0x01, 0x01, 0xef,
  0xf3, 0x27, 0xfb, 0x4e, 0x53, 0x11, 0x40, 0x5b, 0xf2, 0xf9, 0xdb, 0x18,
  0x01, 0x00, 0x04, 0x50, 0x41, 0x4c, 0x08, 0x01, 0x00, 0x3e, 0x00, 0x01,
  0x00, 0x82, 0x3c, 0x82, 0x0e, 0x00, 0x00, 0x00, 0x37, 0x72, 0x5c, 0x0e,
  0x4e, 0x4b, 0xe4, 0xad, 0xee, 0xca, 0x45, 0x69, 0x06, 0x00, 0x00, 0x00,
  0x00, 0x00, 0x00
};
#endif

void main_loop(void)
{
	sdl_context_t* this = &main_ctx;
	SDL_Event e;
	bool quit = false;

	while(SDL_PollEvent(&e)) {
		if (e.type == SDL_QUIT) quit = true;
		if (e.type == SDL_KEYDOWN) {
			switch( e.key.keysym.sym ) {
#ifndef __EMSCRIPTEN__
				case SDLK_ESCAPE: quit = true; break;
				case SDLK_F11: SDL_WM_ToggleFullScreen(this->screen); break;
				case SDLK_F12:
					if (e.key.keysym.mod & KMOD_SHIFT) {
						this->recording_video = !this->recording_video;
					} else {
						this->take_screenshot = true;
					}
				break;
#endif
				case SDLK_r: if (this->retrogress_heap) this->do_retrogress = true; break;
				case SDLK_p: this->pause = !this->pause; break;
				case SDLK_SPACE: this->next_frame = true; this->pause = true; break;
#ifdef __EMSCRIPTEN__
				case SDLK_b:
#else
				case SDLK_t:
					if (e.key.keysym.mod & KMOD_SHIFT) {
#endif
						retro_imitator_init(&this->state, this->rom, this->rom_size);
						if (this->retrogress_heap) {
							(void)WRITE32LE(&this->retrogress_heap[0], (u32)-1);
							(void)WRITE32LE(&this->retrogress_heap[4], (u32)-1);
							this->retrogress_head = 8;
							this->retrogress_tail = 8;
						}
#ifdef __EMSCRIPTEN__
				break;
				case SDLK_t: this->reset_request = true; break;
#else
					} else {
						this->reset_request = true;
					}
				break;
#endif
				case SDLK_s: this->keypad_p1 |= 0x01; break;
				case SDLK_a: this->keypad_p1 |= 0x02; break;
				case SDLK_q: this->keypad_p1 |= 0x04; break;
				case SDLK_w: this->keypad_p1 |= 0x08; break;
				case SDLK_UP: this->keypad_p1 |= 0x10; break;
				case SDLK_DOWN: this->keypad_p1 |= 0x20; break;
				case SDLK_LEFT: this->keypad_p1 |= 0x40; break;
				case SDLK_RIGHT: this->keypad_p1 |= 0x80; break;
				case SDLK_QUOTE: this->keypad_p2 |= 0x01; break;
				case SDLK_SEMICOLON: this->keypad_p2 |= 0x02; break;
				case SDLK_PERIOD: this->keypad_p2 |= 0x04; break;
				case SDLK_SLASH: this->keypad_p2 |= 0x08; break;
				case SDLK_i: this->keypad_p2 |= 0x10; break;
				case SDLK_k: this->keypad_p2 |= 0x20; break;
				case SDLK_j: this->keypad_p2 |= 0x40; break;
				case SDLK_l: this->keypad_p2 |= 0x80; break;

				case SDLK_1: this->state.audio_chn_mute ^= (1<<0); break;
				case SDLK_2: this->state.audio_chn_mute ^= (1<<1); break;
				case SDLK_3: this->state.audio_chn_mute ^= (1<<2); break;
				case SDLK_4: this->state.audio_chn_mute ^= (1<<3); break;
				case SDLK_5: this->state.audio_chn_mute ^= (1<<4); break;
				case SDLK_6: this->state.audio_chn_mute ^= (1<<5); break;
				case SDLK_7: this->state.audio_chn_mute ^= (1<<6); break;
				case SDLK_8: this->state.audio_chn_mute ^= (1<<7); break;

				case SDLK_9: this->state.render_option ^= (1<<0); break;
				case SDLK_MINUS: this->state.render_option ^= (1<<1); break;
				case SDLK_0: this->state.render_option ^= (1<<2); break;
				case SDLK_BACKSPACE: this->state.render_option ^= (1<<3); break;
				case SDLK_BACKSLASH: this->state.render_option ^= (1<<4); break;
				case SDLK_EQUALS: this->state.render_option ^= (1<<5); break;
				default: break;
			}
		}
		if (e.type == SDL_KEYUP) {
			switch( e.key.keysym.sym ) {
				case SDLK_r: this->do_retrogress = false; break;

				case SDLK_s: this->keypad_p1 &= ~0x01; break;
				case SDLK_a: this->keypad_p1 &= ~0x02; break;
				case SDLK_q: this->keypad_p1 &= ~0x04; break;
				case SDLK_w: this->keypad_p1 &= ~0x08; break;
				case SDLK_UP: this->keypad_p1 &= ~0x10; break;
				case SDLK_DOWN: this->keypad_p1 &= ~0x20; break;
				case SDLK_LEFT: this->keypad_p1 &= ~0x40; break;
				case SDLK_RIGHT: this->keypad_p1 &= ~0x80; break;
				case SDLK_QUOTE: this->keypad_p2 &= ~0x01; break;
				case SDLK_SEMICOLON: this->keypad_p2 &= ~0x02; break;
				case SDLK_PERIOD: this->keypad_p2 &= ~0x04; break;
				case SDLK_SLASH: this->keypad_p2 &= ~0x08; break;
				case SDLK_i: this->keypad_p2 &= ~0x10; break;
				case SDLK_k: this->keypad_p2 &= ~0x20; break;
				case SDLK_j: this->keypad_p2 &= ~0x40; break;
				case SDLK_l: this->keypad_p2 &= ~0x80; break;
				default: break;
			}
		}
	}

	// First threshold means only run frames if the queue will contain
	// less then 2 full audio output buffers after running
	bool new_frame = false;
	float audio_buffer[AUDIO_BUF_SIZE*2];
	while (this->audio_buf[0] < (AUDIO_BUF_SIZE*2 + AUDIO_SDL_BUF_SIZE)) {
		memset(audio_buffer, 0, sizeof(float)*AUDIO_BUF_SIZE*2);
		int framerate_mul = 1;
		if (this->do_retrogress) framerate_mul = (this->next_frame) ? -1 : -2;
		if (this->pause && !this->next_frame) framerate_mul = 0;
		while (framerate_mul != 0) {
			this->next_frame = false;

			if (framerate_mul < 0) {
				if (!this->retrogress_heap || (this->retrogress_head == this->retrogress_tail)) break;
				u32 old_head = this->retrogress_head;
				u32 new_head = READ32LE(&this->retrogress_heap[old_head - 8]);
				if (new_head == (u32)-1) break;
				this->retrogress_head = new_head;
				u8 *diff = &this->retrogress_heap[new_head];
				diff += retro_imitator_state_apply_diff(0x6000, this->state.mem, diff);
				diff += retro_imitator_state_apply_diff(0x8000, this->state.mode20_ram, diff);
				retro_imitator_state_restore(&this->state);
			}

			u8 save_state_mem[0x6000+0x8000];
			for (int i = 0; i < 0x6000; i++) save_state_mem[i] = this->state.mem[i];
			for (int i = 0; i < 0x8000; i++) save_state_mem[0x6000+i] = this->state.mode20_ram[i];

			retro_imitator_advance_frame(&this->state, this->keypad_p1|0xff00, this->keypad_p2|0xff00, this->reset_request, this->state.render_option, this->state.audio_chn_mute);
			this->reset_request = false;

			float* audio_buffer_ptr = (framerate_mul == -1) ? audio_buffer+AUDIO_BUF_SIZE : audio_buffer;

			retro_imitator_audio_render(&this->state, audio_buffer_ptr, AUDIO_BUF_SIZE);

			// make internals read only visible at 0x0e00 every nmi and for sprites rendering.
			retro_imitator_state_save(&this->state);

			if (framerate_mul < 0) {
				for (int i = 0; i < AUDIO_BUF_SIZE/2; ++i) {
					float x = audio_buffer_ptr[i];
					audio_buffer_ptr[i] = audio_buffer_ptr[AUDIO_BUF_SIZE-1-i];
					audio_buffer_ptr[AUDIO_BUF_SIZE-1-i] = x;
				}
				for (int i = 0; i < 0x6000; i++) this->state.mem[i] = save_state_mem[i];
				for (int i = 0; i < 0x8000; i++) this->state.mode20_ram[i] = save_state_mem[0x6000+i];
				retro_imitator_state_restore(&this->state);
				++framerate_mul;
				if (framerate_mul == 0) {
					for (int i = 0; i < AUDIO_BUF_SIZE; ++i) audio_buffer[i] = (audio_buffer[i*2+0] + audio_buffer[i*2+1]) / 2.0;
				}
			}

			if (framerate_mul > 0) {
				u32 old_head = this->retrogress_head;
				u8 *diff = &this->retrogress_heap[old_head];
				size_t patch_size = retro_imitator_state_make_diff(diff, 0x6000, save_state_mem, this->state.mem);
				patch_size += retro_imitator_state_make_diff(diff + patch_size, 0x8000, save_state_mem+0x6000, this->state.mode20_ram);
				u32 new_head = old_head + patch_size + 8;
				if (RETROGRESS_HEAP_SIZE-0x9000+8 <= new_head) new_head -= RETROGRESS_HEAP_SIZE-0x9000;
				this->retrogress_head = new_head;
				(void)WRITE32LE(&this->retrogress_heap[old_head-4], new_head);
				(void)WRITE32LE(&this->retrogress_heap[new_head-8], old_head);
				(void)WRITE32LE(&this->retrogress_heap[new_head-4], (u32)-1);

				size_t remaining_space = RETROGRESS_HEAP_SIZE - new_head + (this->retrogress_tail-8) - 0x9000;
				if (new_head < this->retrogress_tail) remaining_space = (this->retrogress_tail-8) - new_head;
				while (remaining_space < 0x9000) {
					// delete old entries until enough space for a new one later
					u32 old_tail = this->retrogress_tail;
					u32 new_tail = READ32LE(&this->retrogress_heap[old_tail - 4]);
					if (new_tail == (u32)-1) break; // uh oh, list empty ?!
					this->retrogress_tail = new_tail;
					(void)WRITE32LE(&this->retrogress_heap[new_tail - 8], (u32)-1);
					if (new_tail < old_tail) break;
					remaining_space += (new_tail - old_tail);
				}
				--framerate_mul;
			}
			new_frame = true;
		}

		SDL_LockAudio();
		{
			int k = this->audio_buf[0];
			for (int i = 0; i < AUDIO_BUF_SIZE; ++i) {
				float x = audio_buffer[i];
				x = (-1.0 <= x) ? x : -1.0;
				x = (1.0 <= x) ? 1.0 : x;
				this->audio_buf[1+k] = x * 32767.0;
				k++;
			}
			this->audio_buf[0] = k;
		}
		SDL_UnlockAudio();

#ifndef __EMSCRIPTEN__
		if (this->take_screenshot) {
			struct timespec ts;
			clock_gettime(CLOCK_REALTIME, &ts);
			struct tm *tm_buf = gmtime(&ts.tv_sec);
			int tv_frame = (ts.tv_nsec*60/1000000000);
			char filename[128];
			snprintf(filename, 128, "%04d-%02d-%02d-%02d-%02d-%02d-%02d_retro-imitator-screenshot.bmp", tm_buf->tm_year+1900, tm_buf->tm_mon+1, tm_buf->tm_mday, tm_buf->tm_hour, tm_buf->tm_min, tm_buf->tm_sec, tv_frame);
			SDL_SaveBMP(this->screen, filename);

			this->take_screenshot = false;
		}

		if (this->recording_video) break;
#endif

		// Second threshold means we're done if we have a 1 full audio output buffer.
		// Conversely, run again if the queue has the opportunity to under-run
		if (AUDIO_BUF_SIZE*2 <= this->audio_buf[0]) break;
	}

	if (new_frame) {
		if (SDL_MUSTLOCK(this->screen)) SDL_LockSurface(this->screen);
		retro_imitator_render(&this->state, (u8*)this->screen->pixels, this->screen->pitch, 316, 270, 30, 15);
		u8 bg_c = retro_imitator_get_bg_color(&this->state);
		SDL_Color p[64];
		for (int c = 0; c < 64; ++c) {
			p[c].r = ((((c & 12) >> 2) * 85) + (((bg_c & 12) >> 2) * 85)) / 2;
			p[c].g = ((((c & 48) >> 4) * 85) + (((bg_c & 48) >> 4) * 85)) / 2;
			p[c].b = ((((c &  3) >> 0) * 85) + (((bg_c &  3) >> 0) * 85)) / 2;
		}
		SDL_SetColors(this->screen, p, 192, 64);
		if (SDL_MUSTLOCK(this->screen)) SDL_UnlockSurface(this->screen);
		SDL_Flip(this->screen);

#ifndef __EMSCRIPTEN__
		if (this->recording_video && !this->video_out_file) {
			if (this->send_video_to_stdout) {
				this->video_out_file = stdout;
			} else {
				struct timespec ts;
				clock_gettime(CLOCK_REALTIME, &ts);
				struct tm *tm_buf = gmtime(&ts.tv_sec);
				int tv_frame = (ts.tv_nsec*60/1000000000);
				char filename[128];
				snprintf(filename, 128, "%04d-%02d-%02d-%02d-%02d-%02d-%02d_retro-imitator_recording.nut", tm_buf->tm_year+1900, tm_buf->tm_mon+1, tm_buf->tm_mday, tm_buf->tm_hour, tm_buf->tm_min, tm_buf->tm_sec, tv_frame);
				this->video_out_file = fopen(filename, "wb");
			}
			if (this->video_out_file) {
				fwrite(video_stream_header, 1, 159, this->video_out_file);
			} else {
				this->recording_video = false;
			}
		}
		if (this->recording_video && this->video_out_file) {
			assert(AUDIO_BUF_SIZE == 512);
			u8 audio_packet[512*2+3];
			u8 video_packet[316*270+1024+4];
			audio_packet[0] = 0x55;
			audio_packet[1] = 0x88;
			audio_packet[2] = 0x00;
			video_packet[0] = 0x56;
			video_packet[1] = 0x85;
			video_packet[2] = 0xa2;
			video_packet[3] = 0x48;
			for (int i = 0; i < AUDIO_BUF_SIZE; ++i) {
				float x = audio_buffer[i];
				x = (-1.0 <= x) ? x : -1.0;
				x = (1.0 <= x) ? 1.0 : x;
				s16 samp = x * 32767.0;
				audio_packet[3 + i*2 + 0] = (samp & 0x00ff) >> 0;
				audio_packet[3 + i*2 + 1] = (samp & 0xff00) >> 8;
			}
			for (int y = 0; y < 270; ++y) {
				for (int x = 0; x < 316; ++x) {
					u8 c = ((u8*)this->screen->pixels)[y * this->screen->pitch + x];
					video_packet[4 + y*316 + x] = c;
				}
			}
			for (int i = 0; i < 256; ++i) {
				SDL_Color c = this->screen->format->palette->colors[i];
				video_packet[4 + 316*270 + i*4 + 0] = c.b;
				video_packet[4 + 316*270 + i*4 + 1] = c.g;
				video_packet[4 + 316*270 + i*4 + 2] = c.r;
				video_packet[4 + 316*270 + i*4 + 3] = 0xff;
			}
			fwrite(audio_packet, 1, 512*2+3, this->video_out_file);
			fwrite(video_packet, 1, 316*270+1024+4, this->video_out_file);
		}
#endif
	}
#ifndef __EMSCRIPTEN__
	if (!new_frame) SDL_Delay(1);  // Save CPU from busy looping
#endif

	if (quit) {
		free(this->retrogress_heap);

		if (this->rom != gDefaultRomData) free(this->rom);
		SDL_CloseAudio();
		SDL_Quit();
#ifdef __EMSCRIPTEN__
		emscripten_cancel_main_loop();
#else
		if (this->video_out_file && (this->video_out_file != stdout)) fclose(this->video_out_file);
		exit(0);
#endif
    }
}

void MyAudioCallback(void* userdata, Uint8* stream, int byte_length)
{
	s16* in = (s16*)userdata;
	s16 in_length = in[0];
	in++;
	s16* out = (s16*)stream;
	int out_length = byte_length/(int)sizeof(s16);

	for (int i = 0; i < out_length; ++i) {
		out[i] = (i < in_length) ? in[i] : 0;
	}
	for (int k = 0, i = out_length; i < in_length; ++i, ++k) {
		in[k] = in[i];
	}
	*(s16*)userdata = (in_length < out_length) ? 0 : (in_length - out_length);
}

int main(int argc, char *argv[])
{
	sdl_context_t* this = &main_ctx;
	memset(this, 0, sizeof(*this));

	this->audio_buf[0] = AUDIO_BUF_SIZE;
	this->error = 0;
	this->pause = false;
	this->keypad_p1 = 0;
	this->keypad_p2 = 0;
	this->reset_request = false;
	this->next_frame = false;
#ifndef __EMSCRIPTEN__
	this->take_screenshot = false;
#endif

	if (argc < 2) {
		this->rom = (u8 *)gDefaultRomData;
		this->rom_size = gDefaultRomSize;
	} else {
		FILE *input_file = fopen(argv[1], "rb");
		if (!input_file) return 1;
		this->rom_size = read_whole_file(&this->rom, input_file);
		fclose(input_file);
		if (this->rom_size < 32) {
			fprintf(stderr, "Failed to load: %s\n", argv[1]);
			return 1;
		}
	}

	this->retrogress_heap = malloc(RETROGRESS_HEAP_SIZE);
	if (this->retrogress_heap) {
		(void)WRITE32LE(&this->retrogress_heap[0], (u32)-1);
		(void)WRITE32LE(&this->retrogress_heap[4], (u32)-1);
		this->retrogress_head = 8;
		this->retrogress_tail = 8;
	}
	this->do_retrogress = false;

#ifndef __EMSCRIPTEN__
	this->send_video_to_stdout = ((argc >= 3) && (argv[argc-1][0] == '-'));
#endif

	retro_imitator_init(&this->state, this->rom, this->rom_size);

	this->error = SDL_Init(SDL_INIT_VIDEO|SDL_INIT_AUDIO);
	if (this->error) {
		fprintf(stderr, "SDL could not initialize! SDL_Error: %s\n", SDL_GetError());
		return 1;
	}
	char window_title_char[1024];
	if (argc < 2) {
		SDL_WM_SetCaption("Retro Imitator", "game");
	} else {
		const char *pch = strrchr(argv[1], '/');
		pch = (pch) ? pch+1 : argv[1];
		snprintf(window_title_char, 1024, "%s - Retro Imitator", pch);
		SDL_WM_SetCaption(window_title_char, "game");
	}

	this->screen = SDL_SetVideoMode(316, 270, 8, SDL_RESIZABLE);
	SDL_Color p[256];
	memset(&p, 0, sizeof(p));
	for (int c = 0; c < 192; ++c) {
		u8 mul = ((c&0xc0) ? 64 : 85);
		u8 add = ((c&0x80) ? 63 : 0);
		p[c].r = ((c & 12) >> 2) * mul + add;
		p[c].g = ((c & 48) >> 4) * mul + add;
		p[c].b = ((c & 3) >> 0) * mul + add;
	}
	for (int c = 192; c < 256; ++c) {
		p[c].r = (((c & 12) >> 2) * 85) / 2;
		p[c].g = (((c & 48) >> 4) * 85) / 2;
		p[c].b = (((c & 3) >> 0) * 85) / 2;
	}
	SDL_SetColors(this->screen, p, 0, 256);

	SDL_AudioSpec main_audio_spec;
	memset(&main_audio_spec, 0, sizeof(main_audio_spec));
	main_audio_spec.freq = RETRO_IMITATOR_AUDIO_FREQ;
	main_audio_spec.format = AUDIO_S16;
	main_audio_spec.channels = 1;
	// main_audio_spec.silence; SDL_OpenAudio() fills in
	main_audio_spec.samples = AUDIO_SDL_BUF_SIZE;
	// main_audio_spec.size; SDL_OpenAudio() fills in
	main_audio_spec.callback = MyAudioCallback;
	main_audio_spec.userdata = this->audio_buf;
	this->error = SDL_OpenAudio(&main_audio_spec, NULL);
	if (this->error) fprintf(stderr, "SDL_OpenAudio: %s\n", SDL_GetError());
	SDL_PauseAudio(0);

#ifdef __EMSCRIPTEN__
	emscripten_set_main_loop(main_loop, 0, 1);
#else
	while (1) { main_loop(); }
#endif

	return 0;
}

#ifdef __EMSCRIPTEN__
EMSCRIPTEN_KEEPALIVE bool retro_imitator_load_game(const char* filename, size_t size, u8 buffer[size]) {
	sdl_context_t* this = &main_ctx;

	if (size < 32) {
		fprintf(stderr, "Failed to load: %s\n", filename);
		return false;
	} else {
		u8* new_rom = malloc(size);
		if (!new_rom) {
			fprintf(stderr, "Failed to load: %s\n", filename);
			return false;
		}
		memcpy(new_rom, buffer, size);
		if (this->rom != gDefaultRomData) free(this->rom);
		this->rom = new_rom;
		this->rom_size = size;

		retro_imitator_init(&this->state, this->rom, this->rom_size);
		char new_window_title[1024];
		const char *pch = strrchr(filename, '/');
		pch = (pch) ? pch+1 : filename;
		snprintf(new_window_title, 1024, "%s - Retro Imitator", pch);
		SDL_WM_SetCaption(new_window_title, "game");
		if (this->retrogress_heap) {
			(void)WRITE32LE(&this->retrogress_heap[0], (u32)-1);
			(void)WRITE32LE(&this->retrogress_heap[4], (u32)-1);
			this->retrogress_head = 8;
			this->retrogress_tail = 8;
		}
	}

	return true;
}
#endif

#endif

