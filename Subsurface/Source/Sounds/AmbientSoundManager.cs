﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Subsurface.Sounds;
using System.Collections.Generic;

namespace Subsurface
{
    public enum DamageSoundType { 
        None, 
        StructureBlunt, StructureSlash, 
        LimbBlunt, LimbSlash, LimbArmor,
        Implode }

    public struct DamageSound
    {
        //the range of inflicted damage where the sound can be played
        //(10.0f, 30.0f) would be played when the inflicted damage is between 10 and 30
        public readonly Vector2 damageRange;

        public readonly DamageSoundType damageType;

        public readonly Sound sound;

        public DamageSound(Sound sound, Vector2 damageRange, DamageSoundType damageType)
        {
            this.sound = sound;
            this.damageRange = damageRange;
            this.damageType = damageType;
        }
    }

    public class BackgroundMusic
    {
        public readonly string file;
        public readonly string type;

        public readonly Vector2 priorityRange;

        public BackgroundMusic(string file, string type, Vector2 priorityRange)
        {
            this.file = file;
            this.type = type;
            this.priorityRange = priorityRange;
        }
    }

    static class AmbientSoundManager
    {
        public static Sound[] flowSounds = new Sound[3];

        private const float MusicLerpSpeed = 0.01f;

        private static Sound waterAmbience;
        private static int waterAmbienceIndex;

        private static DamageSound[] damageSounds;

        private static BackgroundMusic currentMusic;
        private static BackgroundMusic targetMusic;
        private static BackgroundMusic[] musicClips;
        private static float musicVolume;

        private static Sound startDrone;
        
        public static IEnumerable<Status> Init()
        {


            startDrone = Sound.Load("Content/Sounds/startDrone.ogg");
            startDrone.Play();


            yield return Status.Running;


            waterAmbience = Sound.Load("Content/Sounds/Water/WaterAmbience.ogg");
            yield return Status.Running;
            flowSounds[0] = Sound.Load("Content/Sounds/Water/FlowSmall.ogg");
            yield return Status.Running;
            flowSounds[1] = Sound.Load("Content/Sounds/Water/FlowMedium.ogg");
            yield return Status.Running;
            flowSounds[2] = Sound.Load("Content/Sounds/Water/FlowLarge.ogg");
            yield return Status.Running;

            XDocument doc = ToolBox.TryLoadXml("Content/Sounds/Sounds.xml");
            if (doc == null) yield return Status.Failure;

            yield return Status.Running;

            var xMusic = doc.Root.Elements("music").ToList();

            if (xMusic.Any())
            {
                musicClips = new BackgroundMusic[xMusic.Count];
                int i = 0;
                foreach (XElement element in xMusic)
                {
                    string file = ToolBox.GetAttributeString(element, "file", "").ToLower();
                    string type = ToolBox.GetAttributeString(element, "type", "").ToLower();
                    Vector2 priority = ToolBox.GetAttributeVector2(element, "priorityrange", new Vector2(0.0f, 100.0f));

                    musicClips[i] = new BackgroundMusic(file, type, priority);

                    yield return Status.Running;

                    i++;
                }
            }
            
            var xDamageSounds = doc.Root.Elements("damagesound").ToList();
            
            if (xDamageSounds.Any())
            {
                damageSounds = new DamageSound[xDamageSounds.Count()];
                int i = 0;
                foreach (XElement element in xDamageSounds)
                {
                    yield return Status.Running;

                    Sound sound = Sound.Load(ToolBox.GetAttributeString(element, "file", ""));
                    if (sound == null) continue;
                    
                    DamageSoundType damageSoundType = DamageSoundType.None;

                    try
                    {
                       damageSoundType =  (DamageSoundType)Enum.Parse(typeof(DamageSoundType), 
                        ToolBox.GetAttributeString(element, "damagesoundtype", "None"));
                    }
                    catch
                    {
                        damageSoundType = DamageSoundType.None;
                    }


                    damageSounds[i] = new DamageSound(
                        sound, ToolBox.GetAttributeVector2(element, "damagerange", new Vector2(0.0f,100.0f)), damageSoundType);
                    i++;
                }
            }

            yield return Status.Success;

        }
        

        public static void Update()
        {
            UpdateMusic();

            if (startDrone!=null)
            {
                if (!SoundManager.IsPlaying(startDrone.AlBufferId))
                {
                    startDrone.Remove();
                    startDrone = null;
                }
            }

            float ambienceVolume = 0.5f;
            float lowpassHFGain = 1.0f;
            if (Character.Controlled != null)
            {
                AnimController animController = Character.Controlled.AnimController;
                if (animController.HeadInWater)
                {
                    ambienceVolume = 0.5f;
                    ambienceVolume += animController.limbs[0].LinearVelocity.Length();

                    lowpassHFGain = 0.2f;
                }
            }

            SoundManager.LowPassHFGain = lowpassHFGain;
            waterAmbienceIndex = waterAmbience.Loop(waterAmbienceIndex, ambienceVolume);
        }

        private static void UpdateMusic()
        {
            if (musicClips == null) return;
            
            Task criticalTask = null;
            if (Game1.GameSession!=null)
            {
                foreach (Task task in Game1.GameSession.taskManager.Tasks)
                {
                    if (criticalTask == null || task.Priority > criticalTask.Priority)
                    {
                        criticalTask = task;
                    }
                }
            }

            List<BackgroundMusic> suitableMusic = null;
            if (criticalTask == null)
            {
                suitableMusic = musicClips.Where(x => x != null && x.type == "default").ToList();
            }
            else
            {
                suitableMusic = musicClips.Where(x =>
                    x != null &&
                    x.type == criticalTask.MusicType &&
                    x.priorityRange.X < criticalTask.Priority &&
                    x.priorityRange.Y > criticalTask.Priority).ToList();                
            }

            if (suitableMusic.Count > 0 && !suitableMusic.Contains(currentMusic))
            {
                int index = Rand.Int(suitableMusic.Count());

                if (currentMusic == null || suitableMusic[index].file != currentMusic.file)
                {
                    targetMusic = suitableMusic[index];
                }
            }

            if (targetMusic == null || currentMusic == null || targetMusic.file != currentMusic.file)
            {
                musicVolume = MathHelper.Lerp(musicVolume, 0.0f, MusicLerpSpeed);
                if (currentMusic != null) Sound.StreamVolume(musicVolume);

                if (musicVolume < 0.01f)
                {
                    Sound.StopStream();
                    if (targetMusic != null) Sound.StartStream(targetMusic.file, musicVolume);
                    currentMusic = targetMusic;
                }
            }
            else
            {
                musicVolume = MathHelper.Lerp(musicVolume, 0.3f, MusicLerpSpeed);
                Sound.StreamVolume(musicVolume);
            }
        }

        public static void PlayDamageSound(DamageSoundType damageType, float damage, Body body)
        {
            Vector2 bodyPosition = ConvertUnits.ToDisplayUnits(body.Position);
            bodyPosition.Y = -bodyPosition.Y;

            PlayDamageSound(damageType, damage, bodyPosition);
        }

        public static void PlayDamageSound(DamageSoundType damageType, float damage, Vector2 position)
        {
            damage = MathHelper.Clamp(damage, 0.0f, 100.0f);
            var sounds = damageSounds.Where(x => damage >= x.damageRange.X && damage <= x.damageRange.Y && x.damageType == damageType).ToList();
            if (!sounds.Any()) return;

            int selectedSound = Rand.Int(sounds.Count());

            int i = 0;
            foreach (var s in sounds)
            {
                if (i == selectedSound)
                {
                    Debug.WriteLine(s.sound.Play(1.0f, 2000.0f, position));
                    Debug.WriteLine("playing: " + s.sound);
                    return;
                }
                i++;
            }
        }
        
    }
}