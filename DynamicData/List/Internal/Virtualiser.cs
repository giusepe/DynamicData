﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using DynamicData.Annotations;
using DynamicData.Controllers;
using DynamicData.Kernel;

namespace DynamicData.Internal
{
    internal class Virtualiser<T>
    {
        private readonly IObservable<IChangeSet<T>> _source;
        private readonly VirtualisingController _controller;
        private readonly List<T> _all = new List<T>();
        private readonly ChangeAwareList<T> _virtualised = new ChangeAwareList<T>();

        private IVirtualRequest _parameters = new VirtualRequest(0,25);

        public Virtualiser([NotNull] IObservable<IChangeSet<T>> source, [NotNull] VirtualisingController controller)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _source = source;
            _controller = controller;
        }

        public IObservable<IChangeSet<T>> Run()
        {
            var locker = new object();
            var request = _controller.Changed
                .Synchronize(locker)
                .Select(Virtualise);

            var datachanged = _source
                 .Synchronize(locker)
                .Select(Virtualise);

            return request.Merge(datachanged)
                .Where(changes => changes != null && changes.Count != 0);
        }

        private IChangeSet<T> Virtualise(IVirtualRequest request)
        {
            if (request == null || request.StartIndex < 0 || request.Size < 1)
                return null;

            _parameters = request;
            return Virtualise();
        }

        private IChangeSet<T> Virtualise(IChangeSet<T> changeset=null)
        {
            if (changeset != null) _all.Clone(changeset);

            var previous = _virtualised;

            var current = _all.Skip(_parameters.StartIndex)
                .Take(_parameters.Size)
                .ToList();
            
            var adds = current.Except(previous).ToArray();
            var removes = previous.Except(current).ToArray();

            removes.ForEach(t=> { _virtualised.Remove(t); });
            adds.ForEach(t =>
            {
                var index = current.IndexOf(t);
                _virtualised.Insert(index,t);
            });


            var moves = changeset.EmptyIfNull()
                            .Where(change => change.Reason == ListChangeReason.Moved 
                                    && change.MovedWithinRange(_parameters.StartIndex, _parameters.StartIndex + _parameters.Size));

            foreach (var change in moves)
            {
                //check whether an item has moved within the same page
                var currentIndex = change.Item.CurrentIndex - _parameters.StartIndex;
                var previousIndex = change.Item.PreviousIndex - _parameters.StartIndex;
                _virtualised.Move(previousIndex, currentIndex);

            }


            //find updates
            for (var i = 0; i < current.Count; i++)
            {
                var currentItem = current[i];
                var previousItem = previous[i];

                if (ReferenceEquals(currentItem, previousItem))
                    continue;

                var index = _virtualised.IndexOf(currentItem);
                _virtualised.Move(i,index);

            }
            return _virtualised.CaptureChanges();

        }
    }

}
