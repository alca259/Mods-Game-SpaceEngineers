using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using VRage.Collections;
using VRage.Library.Threading;

namespace Phoenix.LaserDrill
{
    public class MyObjectsPool<T> where T : class, new()
    {
        private MyConcurrentQueue<T> m_unused;
        private HashSet<T> m_active;
        private HashSet<T> m_marked;
        private SpinLockRef m_activeLock = new SpinLockRef();

        //  Count of items allowed to store in this pool.
        private int m_baseCapacity;

        public SpinLockRef ActiveLock
        {
            get
            {
                return m_activeLock;
            }
        }

        public HashSetReader<T> ActiveWithoutLock
        {
            get
            {
                return new HashSetReader<T>(m_active);
            }
        }

        public HashSetReader<T> Active
        {
            get
            {
                using (m_activeLock.Acquire())
                {
                    return new HashSetReader<T>(m_active);
                }
            }
        }

        public int ActiveCount
        {
            get
            {
                using (m_activeLock.Acquire())
                {
                    return m_active.Count;
                }
            }
        }

        public int BaseCapacity
        {
            get { return m_baseCapacity; }
        }

        public int Capacity
        {
            get
            {
                using (m_activeLock.Acquire())
                {
                    return m_unused.Count + m_active.Count;
                }
            }
        }

        private MyObjectsPool()
        {
        }

        public MyObjectsPool(int baseCapacity)
        {
            m_baseCapacity = baseCapacity;
            m_unused = new MyConcurrentQueue<T>(m_baseCapacity);
            m_active = new HashSet<T>();
            m_marked = new HashSet<T>();

            for (int i = 0; i < m_baseCapacity; i++)
            {
                m_unused.Enqueue(new T());
            }
        }

        /// <summary>
        /// Returns true when new item was allocated
        /// </summary>
        public bool AllocateOrCreate(out T item)
        {
            bool create = false;
            using (m_activeLock.Acquire())
            {
                create = (m_unused.Count == 0);
                if (create)
                    item = new T();
                else
                    item = m_unused.Dequeue();

                m_active.Add(item);
            }

            return create;
        }

        //  Allocates new object in the pool and returns reference to it.
        //  If pool doesn't have free object (it's full), null is returned. But this shouldn't happen if capacity is chosen carefully.
        public T Allocate(bool nullAllowed = false)
        {
            T item = default(T);
            using (m_activeLock.Acquire())
            {
                if (m_unused.Count > 0)
                {
                    item = m_unused.Dequeue();
                    m_active.Add(item);
                }
            }

            return item;
        }

        //  Deallocates object imediatelly. This is the version that accepts object, and then it find its node.        
        public void Deallocate(T item)
        {
            using (m_activeLock.Acquire())
            {
                m_active.Remove(item);
                m_unused.Enqueue(item);
            }
        }

        //  Marks object for deallocation, but doesn't remove it immediately. Call it during iterating the pool.
        public void MarkForDeallocate(T item)
        {
            using (m_activeLock.Acquire())
            {
                m_marked.Add(item);
            }
        }

        // Marks all active items for deallocation.
        public void MarkAllActiveForDeallocate()
        {
            using (m_activeLock.Acquire())
            {
                m_marked.UnionWith(m_active);
            }
        }

        //  Deallocates objects marked for deallocation. If same object was marked twice or more times for
        //  deallocation, this method will handle it and deallocate it only once (rest is ignored).        
        public void DeallocateAllMarked()
        {
            using (m_activeLock.Acquire())
            {
                foreach (var marked in m_marked)
                {
                    m_active.Remove(marked);
                    m_unused.Enqueue(marked);
                }

                m_marked.Clear();
            }
        }

        //  Deallocates all objects        
        public void DeallocateAll()
        {
            using (m_activeLock.Acquire())
            {
                foreach (var active in m_active)
                {
                    m_unused.Enqueue(active);
                }

                m_active.Clear();
                m_marked.Clear();
            }
        }
    }
}
